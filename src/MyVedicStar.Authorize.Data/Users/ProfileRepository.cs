using Microsoft.EntityFrameworkCore;
using MyVedicStar.Authorize.Data.Persistence;
using MyVedicStar.Authorize.Data.Persistence.Entities;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Data.Users;

internal sealed class ProfileRepository(AuthorizeDbContext db) : IProfileRepository
{
    private async Task<UserEntity> FindAsync(string issuer, string externalId, CancellationToken token)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Issuer == issuer && u.ExternalId == externalId, token)
            ?? throw new ResourceNotFoundException("Your registered profile was not found. Sign in before opening Profile.");
        if (!user.IsActive) throw new AccountInactiveException();
        return user;
    }

    public async Task<ProfileSyncWork> ReadAsync(string issuer, string externalId, CancellationToken token) =>
        ToWork(await FindAsync(issuer, externalId, token));

    public async Task<ProfileSyncWork> UpdateAsync(string issuer, string externalId, long revision,
        IReadOnlyDictionary<string, string?> changes, CancellationToken token)
    {
        var user = await FindAsync(issuer, externalId, token);
        if (revision != user.ProfileRevision) throw new ValidationException("Your profile changed in another session. Reload it before saving.");
        foreach (var (field, value) in changes)
        {
            switch (field)
            {
                case "firstName":
                    if (user.GivenName != value) { user.GivenName = value; user.FirstNameSyncPending = true; }
                    break;
                case "lastName":
                    if (user.FamilyName != value) { user.FamilyName = value; user.LastNameSyncPending = true; }
                    break;
                case "streetAddress": user.StreetAddress = value; break;
                case "streetAddress2": user.StreetAddress2 = value; break;
                case "country": user.Country = value; break;
                case "state": user.State = value; break;
                case "city": user.City = value; break;
                case "postalCode": user.PostalCode = value; break;
                case "email": user.Email = value; break;
                case "phoneNumber": user.PhoneNumber = value; break;
                default: throw new ValidationException("Unknown profile field.");
            }
        }
        user.ProfileManaged = true;
        user.ProfileRevision++;
        user.UpdatedUtc = DateTime.UtcNow;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ValidationException("Your profile changed in another session. Reload it before saving."); }
        return ToWork(user);
    }

    public async Task<ManagedProfile> SynchronizeAsync(string issuer, string externalId,
        Func<ProfileSyncWork, CancellationToken, Task> synchronize, CancellationToken token)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(token);
            // PostgreSQL row locks serialize Graph work across API instances, including concurrent saves.
            var row = await db.Users.FromSqlInterpolated($"SELECT * FROM myvedicstar.\"User\" WHERE \"Issuer\" = {issuer} AND \"ExternalId\" = {externalId} FOR UPDATE")
                .SingleOrDefaultAsync(token) ?? throw new ResourceNotFoundException("Your registered profile was not found.");
            if (!row.IsActive) throw new AccountInactiveException();
            var work = ToWork(row);
            if (work.Profile.NameSyncPending)
            {
                await synchronize(work, token);
                row.FirstNameSyncPending = false; row.LastNameSyncPending = false;
                await db.SaveChangesAsync(token);
            }
            await tx.CommitAsync(token);
            return ToWork(row).Profile;
        });
    }

    public async Task<bool> MarkSynchronizedAsync(string issuer, string externalId, long revision, CancellationToken token)
    {
        var count = await db.Users.Where(u => u.Issuer == issuer && u.ExternalId == externalId && u.IsActive && u.ProfileRevision == revision)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.FirstNameSyncPending, false)
                .SetProperty(u => u.LastNameSyncPending, false), token);
        db.ChangeTracker.Clear();
        return count == 1;
    }

    private static ProfileSyncWork ToWork(UserEntity user) => new(new ManagedProfile(user.Id,
        user.GivenName, user.FamilyName, user.StreetAddress, user.StreetAddress2, user.Country, user.State,
        user.City, user.PostalCode, user.Email, user.PhoneNumber, user.ProfileRevision,
        user.FirstNameSyncPending || user.LastNameSyncPending), user.FirstNameSyncPending, user.LastNameSyncPending);
}
