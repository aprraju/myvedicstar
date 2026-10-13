using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Authorize.Tests.Integration.Postgres;
using MyVedicStar.Settings.Data;
using MyVedicStar.Shared.Exceptions;
namespace MyVedicStar.Settings.Tests.Integration;

public sealed class SettingsPersistenceTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{
    private async Task<SettingsDbContext> Context()
    {
        Skip.IfNot(database.IsAvailable, "Set MYVEDICSTAR_TEST_POSTGRES to enable disposable PostgreSQL tests.");
        var db = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>().UseNpgsql(database.TestConnectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory", "myvedicstarsettings")).Options);
        await db.Database.MigrateAsync(); return db;
    }
    private async Task<Guid> Account()
    {
        await using var scope = database.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IUserRepository>().UpsertAsync(new(Guid.NewGuid().ToString(), "a@example.com", "First", "Last", "https://test.example"), default);
    }
    [SkippableFact]
    public async Task SavedLocations_DuplicateIsIdempotentAndDefaultDeletionIsAtomic()
    {
        await using var db = await Context(); var user = await Account(); var repo = new SettingsRepository(db);
        var location = new SavedLocationData(Guid.Empty, user, "Austin", "Texas", "United States", "US", "78701", 30.2672, -97.7431, "America/Chicago");
        var first = await repo.AddAsync(location, default); var duplicate = await repo.AddAsync(location with { City = "AUSTIN" }, default);
        Assert.Equal(first.LocationId, duplicate.LocationId); Assert.Single(await repo.LocationsAsync(user, default));
        var settings = await repo.SaveAsync(user, "telugu", first.LocationId, 0, default);
        Assert.Equal(first.LocationId, settings.DefaultLocationId);
        var deleted = await repo.DeleteAsync(user, first.LocationId, default);
        Assert.Null(deleted.DefaultLocationId); Assert.Equal("telugu", deleted.DefaultTradition); Assert.Empty(await repo.LocationsAsync(user, default));
    }
    [SkippableFact]
    public async Task SavedLocations_OtherAccountCannotUseOrDeleteLocation()
    {
        await using var db = await Context(); var owner = await Account(); var stranger = await Account(); var repo = new SettingsRepository(db);
        var location = await repo.AddAsync(new(Guid.Empty, owner, "Austin", null, null, null, null, 30.2672, -97.7431, "America/Chicago"), default);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => repo.SaveAsync(stranger, "telugu", location.LocationId, 0, default));
        await repo.DeleteAsync(stranger, location.LocationId, default); Assert.Single(await repo.LocationsAsync(owner, default));
    }
    [SkippableFact]
    public async Task Settings_StaleRevisionCannotOverwriteNewerDefaults()
    {
        await using var db = await Context(); var user = await Account(); var repo = new SettingsRepository(db);
        await repo.SaveAsync(user, "telugu", null, 0, default);
        await Assert.ThrowsAsync<ValidationException>(() => repo.SaveAsync(user, "tamil", null, 0, default));
        Assert.Equal("telugu", (await repo.ReadAsync(user, default)).DefaultTradition);
    }
}
