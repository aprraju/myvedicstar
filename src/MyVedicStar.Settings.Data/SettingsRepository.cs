using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MyVedicStar.Shared.Exceptions;
using Npgsql;
namespace MyVedicStar.Settings.Data;

internal sealed class SettingsRepository(SettingsDbContext db) : ISettingsRepository
{
    public async Task<SettingsData> ReadAsync(Guid userId, CancellationToken token)
    {
        var row = await db.Settings.AsNoTracking().Include(x => x.DefaultLocation).SingleOrDefaultAsync(x => x.UserId == userId, token);
        return row is null ? new(userId, null, null, 0, null) : ToSettings(row);
    }
    public async Task<SettingsData> SaveAsync(Guid userId, string? tradition, Guid? locationId, long revision, CancellationToken token)
    {
        if (locationId is not null && !await db.Locations.AnyAsync(x => x.UserId == userId && x.LocationId == locationId, token))
            throw new ResourceNotFoundException("Choose one of your saved locations.");
        var row = await db.Settings.SingleOrDefaultAsync(x => x.UserId == userId, token);
        if (revision != (row?.Revision ?? 0)) throw Conflict();
        if (row is null) { row = new() { UserId = userId }; db.Settings.Add(row); }
        row.DefaultTradition = tradition; row.DefaultLocationId = locationId; row.Revision++;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw Conflict(); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation }) { throw Conflict(); }
        return await ReadAsync(userId, token);
    }
    public async Task<IReadOnlyList<SavedLocationData>> LocationsAsync(Guid userId, CancellationToken token) =>
        (await db.Locations.AsNoTracking().Where(x => x.UserId == userId).OrderBy(x => x.City).ThenBy(x => x.LocationId).ToListAsync(token)).Select(ToLocation).ToArray();

    public async Task<SavedLocationData> AddAsync(SavedLocationData location, CancellationToken token)
    {
        var identity = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(location.Latitude, 6):F6},{Math.Round(location.Longitude, 6):F6}|{location.TimeZone}");
        var existing = await db.Locations.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == location.UserId && x.Identity == identity, token);
        if (existing is not null) return ToLocation(existing);
        var row = new SavedLocationEntity
        {
            LocationId = Guid.CreateVersion7(),
            UserId = location.UserId,
            City = location.City,
            State = location.State,
            Country = location.Country,
            CountryCode = location.CountryCode,
            PostalCode = location.PostalCode,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            TimeZone = location.TimeZone,
            Identity = identity
        };
        db.Locations.Add(row);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(row).State = EntityState.Detached;
            var winner = await db.Locations.AsNoTracking().SingleAsync(x => x.UserId == location.UserId && x.Identity == identity, token);
            return ToLocation(winner);
        }
        return ToLocation(row);
    }
    public async Task<SettingsData> DeleteAsync(Guid userId, Guid locationId, CancellationToken token)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(token);
            // Serialize deletion against settings saves through the settings row lock.
            await db.Settings.Where(x => x.UserId == userId && x.DefaultLocationId == locationId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultLocationId, (Guid?)null).SetProperty(x => x.Revision, x => x.Revision + 1), token);
            await db.Locations.Where(x => x.UserId == userId && x.LocationId == locationId).ExecuteDeleteAsync(token);
            await tx.CommitAsync(token);
        });
        db.ChangeTracker.Clear();
        return await ReadAsync(userId, token);
    }
    private static ValidationException Conflict() => new("Settings changed in another session. Reload before saving.");
    private static SettingsData ToSettings(SettingsEntity row) => new(row.UserId, row.DefaultTradition, row.DefaultLocationId, row.Revision, row.DefaultLocation is null ? null : ToLocation(row.DefaultLocation));
    private static SavedLocationData ToLocation(SavedLocationEntity row) => new(row.LocationId, row.UserId, row.City, row.State, row.Country, row.CountryCode, row.PostalCode, row.Latitude, row.Longitude, row.TimeZone);
}
