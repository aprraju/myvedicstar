using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyVedicDay.Ai;
using MyVedicDay.Caching;
using MyVedicDay.Location;
using MyVedicDay.Muhurta.Business;
using MyVedicStar.ApiProtection.Configuration;

namespace MyVedicDay.Api.Tests.Integration.Configuration;

/// <summary>
/// appsettings.json holds the shared defaults; appsettings.Development.json and appsettings.Production.json override
/// what differs per environment. Every environment must start (all options validate) and pick up its own values.
/// </summary>
public sealed class EnvironmentSettingsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("Development", "myvedicday:dev:", 1000, new string[0])]
    [InlineData("Production", "myvedicday:prod:", 60, new[] { "10.2.0.0/23" })]
    public async Task EachEnvironment_Starts_WithItsOwnSettings(string environment, string cachePrefix, int publicReadLimit, string[] trustedProxies)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = host.CreateClient();

        using var health = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        var services = host.Services;
        Assert.Equal(cachePrefix, services.GetRequiredService<IOptions<RedisOptions>>().Value.InstanceName);
        var protection = services.GetRequiredService<IOptions<ApiProtectionOptions>>().Value;
        Assert.Equal(publicReadLimit, protection.RateLimiting.PublicRead.PermitLimit);
        Assert.Equal(trustedProxies, protection.TrustedProxies);
    }

    [Fact]
    public void SharedDefaults_AreReadFromSettings()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var services = host.Services;

        var protection = services.GetRequiredService<IOptions<ApiProtectionOptions>>().Value;
        Assert.Equal(65536, protection.RequestSize.StandardPostBytes);
        Assert.Contains("/health/live", protection.HealthCheckPaths);
        Assert.Contains("/health/ready", protection.HealthCheckPaths);
        Assert.Equal("claude-opus-4-6", services.GetRequiredService<IOptions<AiOptions>>().Value.Anthropic.Model);

        var location = services.GetRequiredService<IOptions<LocationOptions>>().Value;
        Assert.Equal((50d, 300d), (location.NearestCityMaxDistanceKm, location.CountryHintMaxDistanceKm));

        var muhurta = services.GetRequiredService<IOptions<MuhurtaOptions>>().Value;
        Assert.Equal((3, 0.6), (muhurta.BestDefaultCount, muhurta.OtherTextMinimumConfidence));
    }

    [Theory]
    [InlineData("/api/v1/panchang/tomorrow?latitude=17.385&longitude=78.4867")]
    [InlineData("/api/v1/panchang/glossary")]
    [InlineData("/api/v1/traditions")]
    public async Task Controllers_RequireAuthentication(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Development", "/api/v1/panchang/today")]
    [InlineData("Development", "/api/v1/locations/resolve")]
    [InlineData("Production", "/api/v1/panchang/today")]
    [InlineData("Production", "/api/v1/locations/resolve")]
    public async Task HomeEndpoints_AllowAnonymousAccess(string environment, string path)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = host.CreateClient();
        // Invalid coordinates reach request validation without authentication or external dependencies.
        using var response = await client.GetAsync(new Uri($"{path}?latitude=95&longitude=0", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
