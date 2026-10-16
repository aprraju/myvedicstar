using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Tests.Integration.Postgres;

public sealed class ManagedProfilePersistenceTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{
    private const string Issuer = "https://profile.test.example";

    private async Task<string> Account()
    {
        Skip.IfNot(database.IsAvailable, "Set MYVEDICSTAR_TEST_POSTGRES to enable disposable PostgreSQL tests.");
        var id = Guid.NewGuid().ToString();
        await using var scope = database.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IUserRepository>().UpsertAsync(new(id, "old@example.com", "First", "Last", Issuer), default);
        return id;
    }

    [SkippableFact]
    public async Task ReadMissingAccount_DoesNotRegisterIt()
    {
        Skip.IfNot(database.IsAvailable, "PostgreSQL required.");
        await using var scope = database.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => scope.ServiceProvider.GetRequiredService<IProfileRepository>().ReadAsync(Issuer, Guid.NewGuid().ToString(), default));
    }

    [SkippableFact]
    public async Task Save_PreservesEditedFieldsAgainstStaleTokenClaimsAndRejectsStaleRevision()
    {
        var id = await Account();
        await using (var scope = database.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IProfileRepository>();
            await repo.UpdateAsync(Issuer, id, 0, new Dictionary<string,string?> { ["firstName"] = "Edited", ["email"] = "edited@example.com", ["city"] = "Austin" }, default);
            await Assert.ThrowsAsync<ValidationException>(() => repo.UpdateAsync(Issuer,id,0,new Dictionary<string,string?> { ["city"] = "Dallas" },default));
        }
        await using (var scope = database.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IUserRepository>().UpsertAsync(new(id,"old@example.com","First","Last",Issuer),default);
        await using var read = database.Services.CreateAsyncScope();
        var profile = (await read.ServiceProvider.GetRequiredService<IProfileRepository>().ReadAsync(Issuer,id,default)).Profile;
        Assert.Equal("Edited",profile.FirstName); Assert.Equal("edited@example.com",profile.Email); Assert.Equal("Austin",profile.City);
    }

    [SkippableFact]
    public async Task FailedGraphWork_RemainsDurableAndRetryClearsOnlyAfterSuccess()
    {
        var id = await Account();
        await using (var scope = database.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IProfileRepository>().UpdateAsync(Issuer,id,0,new Dictionary<string,string?> { ["firstName"] = "Edited" },default);
        await using (var scope = database.Services.CreateAsyncScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IProfileRepository>().SynchronizeAsync(Issuer,id,(_,_) => throw new InvalidOperationException("Graph unavailable"),default));
        await using (var scope = database.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IProfileRepository>();
            Assert.True((await repo.ReadAsync(Issuer,id,default)).Profile.NameSyncPending);
            var result = await repo.SynchronizeAsync(Issuer,id,(work,_) => { Assert.True(work.FirstNamePending); Assert.False(work.LastNamePending); Assert.Equal("Edited",work.Profile.FirstName); return Task.CompletedTask; },default);
            Assert.False(result.NameSyncPending);
        }
        await using var read = database.Services.CreateAsyncScope();
        Assert.False((await read.ServiceProvider.GetRequiredService<IProfileRepository>().ReadAsync(Issuer,id,default)).Profile.NameSyncPending);
    }

    [SkippableFact]
    public async Task DisplayNameEdit_IsPendingOnlyForDisplayNameUntilSynchronized()
    {
        var id = await Account();
        await using (var scope = database.Services.CreateAsyncScope())
        {
            var work = await scope.ServiceProvider.GetRequiredService<IProfileRepository>().UpdateAsync(Issuer, id, 0,
                new Dictionary<string, string?> { ["displayName"] = "New Display" }, default);
            Assert.Equal("New Display", work.Profile.DisplayName);
            Assert.True(work.Profile.NameSyncPending);
            Assert.True(work.DisplayNamePending);
            Assert.False(work.FirstNamePending);
            Assert.False(work.LastNamePending);
        }
        await using (var scope = database.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IProfileRepository>().SynchronizeAsync(Issuer, id,
                (work, _) => { Assert.True(work.DisplayNamePending); return Task.CompletedTask; }, default);
            Assert.False(result.NameSyncPending);
        }
        await using var read = database.Services.CreateAsyncScope();
        var stored = await read.ServiceProvider.GetRequiredService<IProfileRepository>().ReadAsync(Issuer, id, default);
        Assert.False(stored.DisplayNamePending);
        Assert.Equal("New Display", stored.Profile.DisplayName);
    }

    [SkippableFact]
    public async Task Identity_IsScopedByIssuerAndExternalId()
    {
        var owner = await Account(); var stranger = await Account();
        await using var scope = database.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProfileRepository>();
        await repo.UpdateAsync(Issuer,owner,0,new Dictionary<string,string?> { ["city"] = "Private city" },default);
        Assert.Null((await repo.ReadAsync(Issuer,stranger,default)).Profile.City);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => repo.ReadAsync("https://other.example",owner,default));
    }
}
