using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyVedicStar.Authorize.Data;
using MyVedicStar.Authorize.Data.Users;

namespace MyVedicStar.Authorize.Tests.Integration;

/// <summary>Host with <c>FeatureFlags:UserPersistence</c> off and no usable database settings.</summary>
public sealed class PersistenceDisabledApiFactory : AuthorizeApiFactory
{
    protected override bool UserPersistenceEnabled => false;

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Would fail startup validation if any database service were registered.
        builder.UseSetting("Database:ConnectionString", string.Empty);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
    }
}

public sealed class UserPersistenceDisabledTests : IClassFixture<PersistenceDisabledApiFactory>
{
    private const string ObjectId = "11111111-2222-3333-4444-555555555555";

    private readonly PersistenceDisabledApiFactory _factory;
    private readonly HttpClient _client;

    public UserPersistenceDisabledTests(PersistenceDisabledApiFactory factory)
    {
        _factory = factory;
        _factory.ResetFakes();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UserInfo_PersistenceDisabled_ReturnsTokenProfileWithObjectIdAsId()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _factory.CreateToken([("oid", ObjectId), ("name", "John Smith"), ("email", "john.smith@example.com")]));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ObjectId, body.GetProperty("id").GetString());
        Assert.Equal("John Smith", body.GetProperty("name").GetString());
        Assert.Equal("john.smith@example.com", body.GetProperty("email").GetString());
    }

    [Fact]
    public void PersistenceDisabled_RegistersNoDatabaseServicesOrMigrationRunner()
    {
        var services = _factory.Services;

        Assert.Null(services.GetService<IUserRepository>());
        Assert.Null(services.GetService<IUserLoginRepository>());
        Assert.Null(services.GetService<IAuthorizeUnitOfWork>());
        Assert.DoesNotContain(
            services.GetServices<IHostedService>(),
            hostedService => hostedService.GetType().Assembly == typeof(IAuthorizeUnitOfWork).Assembly);
    }
}
