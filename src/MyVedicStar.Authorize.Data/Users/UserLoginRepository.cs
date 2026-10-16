using MyVedicStar.Authorize.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace MyVedicStar.Authorize.Data.Users;

internal sealed class UserLoginRepository : IUserLoginRepository
{
    // One round trip: the session unique index (UX_loginaudit_User_Session_Type) makes repeats a no-op instead of
    // a SELECT before every INSERT. A null session key never conflicts, so legacy callers still append each event.
    private const string InsertOnceSql = """
        INSERT INTO myvedicstar."loginaudit" ("UserId", "Type", "SessionKey", "IpAddress")
        VALUES (@userId, @type, @sessionKey, @ipAddress)
        ON CONFLICT ("UserId", "SessionKey", "Type") DO NOTHING
        """;

    private readonly AuthorizeDbContext _dbContext;

    public UserLoginRepository(AuthorizeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(UserLoginData login, CancellationToken cancellationToken) =>
        _dbContext.Database.ExecuteSqlRawAsync(InsertOnceSql,
        [
            new NpgsqlParameter("userId", NpgsqlDbType.Uuid) { Value = login.UserId },
            new NpgsqlParameter("type", NpgsqlDbType.Integer) { Value = login.Type },
            new NpgsqlParameter("sessionKey", NpgsqlDbType.Varchar) { Value = (object?)login.SessionKey ?? DBNull.Value },
            new NpgsqlParameter("ipAddress", NpgsqlDbType.Inet) { Value = (object?)login.IpAddress ?? DBNull.Value },
        ], cancellationToken);
}
