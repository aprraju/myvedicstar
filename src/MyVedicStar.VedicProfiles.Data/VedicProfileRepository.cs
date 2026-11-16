using Microsoft.EntityFrameworkCore;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Business;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.VedicProfiles.Data;

/// <summary>Owner-scoped persistence. Every keyed query filters accountId AND id; Self uniqueness
/// is protected by the filtered unique index and surfaced as a safe 409 conflict.</summary>
internal sealed class VedicProfileRepository(VedicProfilesDbContext context) : IVedicProfileRepository
{
    private const string UniqueViolation = "23505";

    public async Task<IReadOnlyList<VedicProfileSummary>> ListAsync(Guid accountId, CancellationToken token) =>
        await context.Profiles.AsNoTracking()
            .Where(x => x.UserId == accountId)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new VedicProfileSummary(x.Id, x.Name, x.Relationship, x.CustomRelationship, x.Revision))
            .ToListAsync(token);

    public async Task<VedicProfile?> GetAsync(Guid accountId, Guid id, CancellationToken token)
    {
        var entity = await context.Profiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == accountId && x.Id == id, token);
        return entity is null ? null : ToContract(entity);
    }

    public async Task<VedicProfile> CreateAsync(Guid accountId, VedicProfileDetails details, CancellationToken token)
    {
        var entity = new VedicProfileEntity { Id = Guid.NewGuid(), UserId = accountId, Revision = 1 };
        Apply(entity, details);
        context.Profiles.Add(entity);
        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: UniqueViolation })
        {
            context.Entry(entity).State = EntityState.Detached;
            throw new VedicProfileConflictException(VedicProfileErrors.DuplicateSelf);
        }
        return ToContract(entity);
    }

    public async Task<VedicProfile> ReplaceAsync(Guid accountId, Guid id, VedicProfileDetails details, long revision, CancellationToken token)
    {
        var entity = await context.Profiles.SingleOrDefaultAsync(x => x.UserId == accountId && x.Id == id, token)
            ?? throw new ResourceNotFoundException(VedicProfileErrors.NotFound);
        if (entity.Revision != revision) throw new VedicProfileConflictException(VedicProfileErrors.StaleRevision);
        if (revision == long.MaxValue) throw new VedicProfileConflictException(VedicProfileErrors.StaleRevision);

        Apply(entity, details);
        entity.Revision = revision + 1;
        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new VedicProfileConflictException(VedicProfileErrors.StaleRevision);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: UniqueViolation })
        {
            throw new VedicProfileConflictException(VedicProfileErrors.DuplicateSelf);
        }
        return ToContract(entity);
    }

    public async Task<bool> DeleteAsync(Guid accountId, Guid id, CancellationToken token)
    {
        var entity = await context.Profiles.SingleOrDefaultAsync(x => x.UserId == accountId && x.Id == id, token);
        if (entity is null) return false;
        context.Profiles.Remove(entity);
        await context.SaveChangesAsync(token);
        return true;
    }

    private static void Apply(VedicProfileEntity entity, VedicProfileDetails details)
    {
        entity.Name = details.Name ?? "";
        entity.DateOfBirth = details.DateOfBirth;
        entity.BirthTimeMode = details.BirthTimeMode;
        entity.BirthTime = details.BirthTime;
        entity.BirthTimeFrom = details.BirthTimeFrom;
        entity.BirthTimeTo = details.BirthTimeTo;
        entity.TimeAccuracyNote = details.TimeAccuracyNote;
        entity.LocationName = details.LocationName;
        entity.Latitude = details.Latitude;
        entity.Longitude = details.Longitude;
        entity.TimezoneId = details.TimezoneId;
        entity.Gender = details.Gender;
        entity.Occupation = details.Occupation;
        entity.Relationship = details.Relationship;
        entity.CustomRelationship = details.CustomRelationship;
    }

    private static VedicProfile ToContract(VedicProfileEntity entity) => new(entity.Id,
        new VedicProfileDetails(entity.Name, entity.DateOfBirth, entity.BirthTimeMode,
            entity.BirthTime, entity.BirthTimeFrom, entity.BirthTimeTo, entity.TimeAccuracyNote,
            entity.LocationName, entity.Latitude, entity.Longitude, entity.TimezoneId,
            entity.Gender, entity.Occupation, entity.Relationship, entity.CustomRelationship),
        entity.Revision);
}
