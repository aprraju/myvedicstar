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

    public async Task<Guid> UpsertAsync(UserProfileData profile, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(profile, cancellationToken);
        if (existing is not null) return ActiveId(existing);

        var created = new UserEntity
        {
            Id = Guid.CreateVersion7(), ExternalId = profile.ExternalId, Issuer = profile.Issuer,
            Email = profile.Email, Name = profile.Name, IsActive = true,
        };
        _dbContext.Users.Add(created);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return created.Id;
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UserEntityConfiguration.ExternalIdUniqueIndexName })
        {
            // SaveChanges rolls back to its transaction savepoint; retrieve the winning registration.
            _dbContext.Entry(created).State = EntityState.Detached;
            return ActiveId(await FindAsync(profile, cancellationToken)
                ?? throw new DataAccessException("Your details could not be saved. Please try again later."));
        }
    }

    public Task<Guid?> FindIdAsync(string issuer, string externalId, CancellationToken cancellationToken) =>
        _dbContext.Users.Where(user => user.Issuer == issuer && user.ExternalId == externalId)
            .Select(user => (Guid?)user.Id).SingleOrDefaultAsync(cancellationToken);

    private Task<UserEntity?> FindAsync(UserProfileData profile, CancellationToken cancellationToken) =>
        _dbContext.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Issuer == profile.Issuer && user.ExternalId == profile.ExternalId, cancellationToken);

    private static Guid ActiveId(UserEntity user) => user.IsActive ? user.Id : throw new AccountInactiveException();
}
