using Microsoft.EntityFrameworkCore;
using MyVedicStar.Authorize.Data.Persistence;
using MyVedicStar.Authorize.Data.Persistence.Configurations;
using MyVedicStar.Authorize.Data.Persistence.Entities;
using Npgsql;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Data.Users;

internal sealed class UserRepository : IUserRepository
{
    private readonly AuthorizeDbContext _dbContext;

    public UserRepository(AuthorizeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RegisteredUser> UpsertAsync(UserProfileData profile, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(profile, cancellationToken);
        if (existing is not null) return await UpdateProfileAsync(existing, profile, cancellationToken);

        var created = new UserEntity
        {
            Id = Guid.CreateVersion7(),
            ExternalId = profile.ExternalId,
            Issuer = profile.Issuer,
            Email = profile.Email,
            GivenName = profile.GivenName,
            FamilyName = profile.FamilyName,
            DisplayName = profile.DisplayName,
            IsActive = true,
        };
        _dbContext.Users.Add(created);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ToRegisteredUser(created);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UserEntityConfiguration.ExternalIdUniqueIndexName })
        {
            // SaveChanges rolls back to its transaction savepoint; retrieve the winning registration.
            _dbContext.Entry(created).State = EntityState.Detached;
            var winner = await FindAsync(profile, cancellationToken)
                ?? throw new DataAccessException("Your details could not be saved. Please try again later.");
            return await UpdateProfileAsync(winner, profile, cancellationToken);
        }
    }

    public Task<Guid?> FindIdAsync(string issuer, string externalId, CancellationToken cancellationToken) =>
        _dbContext.Users.Where(user => user.Issuer == issuer && user.ExternalId == externalId)
            .Select(user => (Guid?)user.Id).SingleOrDefaultAsync(cancellationToken);

    private Task<UserEntity?> FindAsync(UserProfileData profile, CancellationToken cancellationToken) =>
        _dbContext.Users.SingleOrDefaultAsync(user => user.Issuer == profile.Issuer && user.ExternalId == profile.ExternalId, cancellationToken);

    private async Task<RegisteredUser> UpdateProfileAsync(UserEntity user, UserProfileData profile, CancellationToken cancellationToken)
    {
        if (!user.IsActive) throw new AccountInactiveException();
        var changed = false;
        if (!user.ProfileManaged && (user.GivenName != profile.GivenName || user.FamilyName != profile.FamilyName || user.Email != profile.Email))
        {
            user.GivenName = profile.GivenName;
            user.FamilyName = profile.FamilyName;
            user.Email = profile.Email;
            changed = true;
        }
        // A token without a usable display name never erases one. A display name edited through the profile is
        // authoritative (and pending for Graph), so the token only fills it in while none is stored.
        if (profile.DisplayName is not null && user.DisplayName != profile.DisplayName && (!user.ProfileManaged || user.DisplayName is null))
        {
            user.DisplayName = profile.DisplayName;
            changed = true;
        }
        if (changed)
        {
            user.UpdatedUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return ToRegisteredUser(user);
    }

    private static RegisteredUser ToRegisteredUser(UserEntity user) => new(user.Id, user.DisplayName, user.GivenName, user.FamilyName);
}
