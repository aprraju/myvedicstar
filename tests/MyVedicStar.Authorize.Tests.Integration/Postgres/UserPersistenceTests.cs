using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Authorize.Data;
using MyVedicStar.Authorize.Data.Persistence;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Shared.Exceptions;
using Npgsql;

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
            .SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'myvedicstar' AND indexname = 'UX_User_Issuer_ExternalId'")
            .ToListAsync();

        Assert.Contains("User", tables);
        Assert.Contains("loginaudit", tables);
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
        Assert.True(stored.IsActive);
    }

    [SkippableFact]
    public async Task Upsert_ExistingUser_UpdatesProfileAndPreservesIdAndCreatedTime()
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
        Assert.True(stored.IsActive);
    }

    [SkippableFact]
    public async Task Upsert_ResolvedNullNameAndEmailFallback_ReplaceStaleProfile()
    {
        SkipIfUnavailable();
        var original = CreateProfile() with { Name = "unknown", Email = null };
        var id = await UpsertInNewScopeAsync(original);
        Assert.Equal(id, await UpsertInNewScopeAsync(original with { Name = null, Email = "aprraju@gmail.com" }));
        var stored = await FindUserAsync(original.ExternalId);
        Assert.Null(stored!.Name);
        Assert.Equal("aprraju@gmail.com", stored.Email);
        Assert.True(stored.IsActive);
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

    [SkippableFact]
    public async Task InactiveAccount_IsNotReactivatedOrAudited()
    {
        SkipIfUnavailable();
        var profile = CreateProfile();
        var id = await UpsertInNewScopeAsync(profile);
        await using (var scope = _database.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthorizeDbContext>();
            var user = await db.Users.SingleAsync(user => user.Id == id);
            user.IsActive = false;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<AccountInactiveException>(() => SignInInNewScopeAsync(profile, IPAddress.Loopback));
        await using var verification = _database.Services.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<AuthorizeDbContext>();
        Assert.False((await context.Users.SingleAsync(user => user.Id == id)).IsActive);
        Assert.False(await context.UserLogins.AnyAsync(entry => entry.UserId == id));
    }

    [SkippableFact]
    public async Task SameExternalId_DifferentIssuers_CreateDistinctAccounts()
    {
        SkipIfUnavailable();
        var profile = CreateProfile() with { Issuer = "https://accounts.google.com" };
        var google = await UpsertInNewScopeAsync(profile);
        var apple = await UpsertInNewScopeAsync(profile with { Issuer = "https://appleid.apple.com" });
        Assert.NotEqual(google, apple);
    }

    [SkippableFact]
    public async Task ConcurrentSessionEvents_AreIdempotentAndNewSessionsAppendHistory()
    {
        SkipIfUnavailable();
        var profile = CreateProfile();
        async Task<Guid> Event(string session, int type)
        {
            await using var scope = _database.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IAuthorizeUnitOfWork>().ExecuteInTransactionAsync(async token =>
            {
                var id = await scope.ServiceProvider.GetRequiredService<IUserRepository>().UpsertAsync(profile, token);
                await scope.ServiceProvider.GetRequiredService<IUserLoginRepository>().AddAsync(new UserLoginData(id, null, session, type), token);
                return id;
            }, default);
        }
        var ids = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Event("session-1", 1)));
        Assert.Single(ids.Distinct());
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Event("session-1", 99)));
        await Event("session-2", 1);
        await using var verification = _database.Services.CreateAsyncScope();
        var events = await verification.ServiceProvider.GetRequiredService<AuthorizeDbContext>().UserLogins
            .Where(entry => entry.UserId == ids[0]).OrderBy(entry => entry.Id).ToListAsync();
        Assert.Equal([1, 99, 1], events.Select(entry => entry.Type));
        Assert.All(events, entry => Assert.Equal(DateTimeKind.Utc, entry.LoginTimeUtc.Kind));
    }

    [SkippableFact]
    public async Task Upgrade_PreservesLegacyAccountAndAuditHistory()
    {
        SkipIfUnavailable();
        var databaseName = "myvedicstar_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = _database.CreateConnection();
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            await using var connection = _database.CreateConnection(databaseName);
            var options = new DbContextOptionsBuilder<AuthorizeDbContext>();
            options.UseNpgsql(connection, provider => provider.MigrationsHistoryTable(AuthorizeDbContext.MigrationsHistoryTable, AuthorizeDbContext.Schema));
            await using var db = new AuthorizeDbContext(options.Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261011000000_AddHybridCacheEntries");
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO myvedicstar."User" ("Id", "ExternalId", "Name", "Email")
                VALUES ({id}, 'legacy-user', 'Original Name', 'original@example.com')
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO myvedicstar."UserLogin" ("UserId") VALUES ({id})
                """);
            await migrator.MigrateAsync();
            var user = await db.Users.SingleAsync();
            Assert.Equal(id, user.Id);
            Assert.Equal("Original Name", user.Name);
            Assert.True(user.IsActive);
            Assert.Equal("https://cbece845-e124-4de7-89d3-96a71abb7b83.ciamlogin.com/cbece845-e124-4de7-89d3-96a71abb7b83/v2.0", user.Issuer);
            var audit = await db.UserLogins.SingleAsync();
            Assert.Equal(id, audit.UserId);
            Assert.Equal(1, audit.Type);
            Assert.Null(audit.SessionKey);
            var repository = new Data.Users.UserRepository(db);
            Assert.Equal(id, await repository.UpsertAsync(new UserProfileData(user.ExternalId, "changed@example.com", "Changed", user.Issuer), default));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
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
