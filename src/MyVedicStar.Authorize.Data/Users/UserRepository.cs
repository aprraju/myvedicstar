using Microsoft.EntityFrameworkCore;
using MyVedicStar.Authorize.Data.Persistence;
using MyVedicStar.Authorize.Data.Persistence.Configurations;
using MyVedicStar.Authorize.Data.Persistence.Entities;
using Npgsql;

namespace MyVedicStar.Authorize.Data.Users;

internal sealed class UserRepository : IUserRepository
{
    // A single INSERT ... ON CONFLICT statement is atomic, so concurrent first sign-ins for the same user
    // cannot create duplicates. Identifiers come from the entity model; all values are parameters.
    private static readonly string UpsertSql = $"""
        INSERT INTO "{AuthorizeDbContext.Schema}"."{UserEntityConfiguration.TableName}" (
            "{nameof(UserEntity.Id)}", "{nameof(UserEntity.ExternalId)}", "{nameof(UserEntity.Email)}",
            "{nameof(UserEntity.Name)}", "{nameof(UserEntity.CreatedUtc)}", "{nameof(UserEntity.UpdatedUtc)}")
        VALUES (@id, @externalId, @email, @name, now(), now())
        ON CONFLICT ("{nameof(UserEntity.ExternalId)}") DO UPDATE SET
            "{nameof(UserEntity.Email)}" = EXCLUDED."{nameof(UserEntity.Email)}",
            "{nameof(UserEntity.Name)}" = EXCLUDED."{nameof(UserEntity.Name)}",
            "{nameof(UserEntity.UpdatedUtc)}" = now()
        RETURNING "{nameof(UserEntity.Id)}" AS "Value"
        """;

    private readonly AuthorizeDbContext _dbContext;

    public UserRepository(AuthorizeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Guid> UpsertAsync(UserProfileData profile, CancellationToken cancellationToken)
    {
        var userIds = await _dbContext.Database
            .SqlQueryRaw<Guid>(
                UpsertSql,
                new NpgsqlParameter("id", Guid.CreateVersion7()),
                new NpgsqlParameter("externalId", profile.ExternalId),
                CreateNullableParameter("email", profile.Email),
                CreateNullableParameter("name", profile.Name))
            .ToListAsync(cancellationToken);

        return userIds.Single();
    }

    private static NpgsqlParameter CreateNullableParameter(string name, string? value) =>
        new(name, NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)value ?? DBNull.Value };
}
