using MyVedicStar.Authorize.Data.Persistence;
using MyVedicStar.Authorize.Data.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using MyVedicStar.Authorize.Data.Persistence.Configurations;
using Npgsql;

namespace MyVedicStar.Authorize.Data.Users;

internal sealed class UserLoginRepository : IUserLoginRepository
{
    private readonly AuthorizeDbContext _dbContext;

    public UserLoginRepository(AuthorizeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(UserLoginData login, CancellationToken cancellationToken)
    {
        if (login.SessionKey is not null && await _dbContext.UserLogins.AnyAsync(entry =>
            entry.UserId == login.UserId && entry.SessionKey == login.SessionKey && entry.Type == login.Type, cancellationToken)) return;
        var audit = new UserLoginEntity
        {
            UserId = login.UserId, IpAddress = login.IpAddress, SessionKey = login.SessionKey, Type = login.Type,
        };
        _dbContext.UserLogins.Add(audit);
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UserLoginEntityConfiguration.SessionUniqueIndexName })
        {
            _dbContext.Entry(audit).State = EntityState.Detached;
        }
    }
}
