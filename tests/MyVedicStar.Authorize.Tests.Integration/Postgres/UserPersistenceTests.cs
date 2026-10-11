using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Authorize.Data;
using MyVedicStar.Authorize.Data.Persistence;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Tests.Integration.Postgres;

public sealed class UserPersistenceTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _database;

    public UserPersistenceTests(PostgresDatabaseFixture database)
    {
        _database = database;
    }

    [SkippableFact]
    public async Task Migration_CreatesTablesAndUniqueExternalIdIndexInMyVedicStarSchema()
    {
        SkipIfUnavailable();
        await using var scope = _database.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthorizeDbContext>();

        var tables = await dbContext.Database
            .SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'myvedicstar'")
            .ToListAsync();
        var externalIdIndex = await dbContext.Database
            .SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'myvedicstar' AND indexname = 'UX_User_ExternalId'")
            .ToListAsync();

        Assert.Contains("User", tables);
        Assert.Contains("UserLogin", tables);
        Assert.Contains("__EFMigrationsHistory", tables);
        Assert.StartsWith("CREATE UNIQUE INDEX", Assert.Single(externalIdIndex));
    }

    [SkippableFact]
    public async Task Upsert_NewUser_InsertsProfile()
    {
        SkipIfUnavailable();
        var profile = CreateProfile();

        var userId = await UpsertInNewScopeAsync(profile);

        var stored = await FindUserAsync(profile.ExternalId);
        Assert.NotNull(stored);
        Assert.Equal(userId, stored.Id);
        Assert.Equal("john.smith@example.com", stored.Email);
        Assert.Equal("John Smith", stored.Name);
        Assert.Equal(DateTimeKind.Utc, stored.CreatedUtc.Kind);
    }

    [SkippableFact]
    public async Task Upsert_ExistingUser_UpdatesProfileAndKeepsIdAndCreatedTime()
    {
        SkipIfUnavailable();
        var original = CreateProfile();
        var firstId = await UpsertInNewScopeAsync(original);
        var created = (await FindUserAsync(original.ExternalId))!.CreatedUtc;

        var secondId = await UpsertInNewScopeAsync(original with { Name = "Jonathan", Email = "new@example.com" });

        var stored = await FindUserAsync(original.ExternalId);
        Assert.Equal(firstId, secondId);
        Assert.Equal("Jonathan", stored!.Name);
        Assert.Equal("new@example.com", stored.Email);
        Assert.Equal(created, stored.CreatedUtc);
        Assert.True(stored.UpdatedUtc >= stored.CreatedUtc);
    }

    [SkippableFact]
    public async Task Upsert_ValuesAtPublishedMaximumLengths_AreStored()
    {
        SkipIfUnavailable();
        var profile = new UserProfileData(
            ExternalId: Guid.NewGuid().ToString().PadRight(UserProfileData.MaxExternalIdLength, 'x'),
            Email: new string('e', UserProfileData.MaxEmailLength),
            Name: new string('n', UserProfileData.MaxNameLength));

        await UpsertInNewScopeAsync(profile);

        var stored = await FindUserAsync(profile.ExternalId);
        Assert.Equal(profile.Email, stored?.Email);
        Assert.Equal(profile.Name, stored?.Name);
    }

    [SkippableFact]
    public async Task Upsert_ConcurrentFirstSignIns_CreateExactlyOneUser()
    {
        SkipIfUnavailable();
        var profile = CreateProfile();

        var ids = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => UpsertInNewScopeAsync(profile)));

        Assert.Single(ids.Distinct());
        await using var scope = _database.Services.CreateAsyncScope();
        var count = await scope.ServiceProvider.GetRequiredService<AuthorizeDbContext>().Users
            .CountAsync(user => user.ExternalId == profile.ExternalId);
        Assert.Equal(1, count);
    }

    [SkippableFact]
    public async Task Transaction_UpsertAndLogin_CommitsBothWithServerUtcTimeAndIp()
    {
        SkipIfUnavailable();
        var profile = CreateProfile();
        var before = DateTime.UtcNow.AddSeconds(-30);

        var userId = await SignInInNewScopeAsync(profile, IPAddress.Parse("203.0.113.7"));

        await using var scope = _database.Services.CreateAsyncScope();
        var login = await scope.ServiceProvider.GetRequiredService<AuthorizeDbContext>().UserLogins
            .SingleAsync(entry => entry.UserId == userId);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), login.IpAddress);
        Assert.Equal(DateTimeKind.Utc, login.LoginTimeUtc.Kind);
        Assert.InRange(login.LoginTimeUtc, before, DateTime.UtcNow.AddSeconds(30));
    }

    [SkippableFact]
    public async Task Transaction_FailureAfterUpsert_RollsBackUser()
    {
        SkipIfUnavailable();
        var profile = CreateProfile();

        await using (var scope = _database.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IAuthorizeUnitOfWork>();
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

            await Assert.ThrowsAsync<DataAccessException>(() => unitOfWork.ExecuteInTransactionAsync<Guid>(
                async token =>
                {
                    await users.UpsertAsync(profile, token);
                    throw new InvalidOperationException("Simulated failure before the login is written.");
                },
                CancellationToken.None));
        }

        Assert.Null(await FindUserAsync(profile.ExternalId));
    }

    [SkippableFact]
    public async Task Login_ForUnknownUser_IsRejectedByForeignKey()
    {
        SkipIfUnavailable();
        await using var scope = _database.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IAuthorizeUnitOfWork>();
        var logins = scope.ServiceProvider.GetRequiredService<IUserLoginRepository>();

        await Assert.ThrowsAsync<DataAccessException>(() => unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await logins.AddAsync(new UserLoginData(Guid.NewGuid(), IpAddress: null), token);
                return true;
            },
            CancellationToken.None));
    }

    private void SkipIfUnavailable() =>
        Skip.IfNot(_database.IsAvailable, $"Set {PostgresDatabaseFixture.ConnectionStringVariable} to run PostgreSQL tests.");

    private static UserProfileData CreateProfile() => new(
        ExternalId: Guid.NewGuid().ToString(),
        Email: "john.smith@example.com",
        Name: "John Smith");

    private async Task<Guid> UpsertInNewScopeAsync(UserProfileData profile)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IUserRepository>().UpsertAsync(profile, CancellationToken.None);
    }

    private async Task<Guid> SignInInNewScopeAsync(UserProfileData profile, IPAddress ipAddress)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IAuthorizeUnitOfWork>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var logins = scope.ServiceProvider.GetRequiredService<IUserLoginRepository>();

        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var userId = await users.UpsertAsync(profile, token);
                await logins.AddAsync(new UserLoginData(userId, ipAddress), token);
                return userId;
            },
            CancellationToken.None);
    }

    private async Task<Data.Persistence.Entities.UserEntity?> FindUserAsync(string externalId)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuthorizeDbContext>().Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.ExternalId == externalId);
    }
}
