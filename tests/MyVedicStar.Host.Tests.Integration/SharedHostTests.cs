extern alias SharedHost;

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.Authorize.Business;
using HostProgram = SharedHost::Program;

namespace MyVedicStar.Host.Tests.Integration;

public sealed class SharedHostTests
{
    [Fact]
    public async Task BothApis_AcceptTheSameAudienceAndPreserveTheirRoutes()
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        var star = await client.PostAsync("/api/v1/auth/userinfo", null);
        Assert.Equal(HttpStatusCode.OK, star.StatusCode);
        using var body = JsonDocument.Parse(await star.Content.ReadAsStringAsync());
        Assert.Equal(["id", "name", "email"], body.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("shared-user", body.RootElement.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/muhurta/activities")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/muhurta/good-times?latitude=17.385&longitude=78.4867")).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/userinfo", true)]
    [InlineData("/api/v1/muhurta/good-times?latitude=17.385&longitude=78.4867", false)]
    public async Task ProtectedRoutes_RejectAnonymousRequests(string path, bool post)
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        var response = post ? await client.PostAsync(path, null) : await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongAudience_IsRejectedByBothApis()
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("wrong-audience"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/userinfo", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/muhurta/good-times?latitude=17.385&longitude=78.4867")).StatusCode);
    }

    [Fact]
    public async Task ValidTokensWithoutTheRequiredScope_AreForbiddenByBothApis()
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(scope: null));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/v1/auth/userinfo", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/muhurta/good-times?latitude=17.385&longitude=78.4867")).StatusCode);
    }

    [Fact]
    public async Task OpenApi_ContainsBothApisWithoutDuplicateInfrastructure()
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/auth/userinfo", out _));
        Assert.True(paths.TryGetProperty("/api/v1/muhurta/activities", out _));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task StarDependencyFailure_UsesCommonErrorsAndDayStillWorks()
    {
        await using var factory = new CombinedFactory(failStar: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "shared-host-error");
        var response = await client.PostAsync("/api/v1/auth/userinfo", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("A dependency is temporarily unavailable. Please retry later.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("shared-host-error", problem.RootElement.GetProperty("correlationId").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        Assert.DoesNotContain("internal database details", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/muhurta/activities")).StatusCode);
    }

    [Fact]
    public async Task GlobalRateLimit_CountsTheSameUserAcrossBothApis()
    {
        await using var factory = new CombinedFactory(globalLimit: 2);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/userinfo", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/muhurta/activities")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/v1/auth/userinfo", null)).StatusCode);
    }

    [Fact]
    public async Task Readiness_IncludesBothEnabledDependencies()
    {
        await using var factory = new CombinedFactory(persistence: true);
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>().Value;
        Assert.Contains(options.Registrations, check => check.Name == "distributed-cache" && check.Tags.Contains("ready"));
        Assert.Contains(options.Registrations, check => check.Name == "authorize-database" && check.Tags.Contains("ready"));
    }

    private sealed class CombinedFactory(bool failStar = false, int globalLimit = 1000, bool persistence = false) : WebApplicationFactory<HostProgram>
    {
        private const string Issuer = "https://test.ciamlogin.com/test/v2.0";
        private const string Audience = "shared-api-audience";
        private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "shared-key" };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            foreach (var setting in new Dictionary<string, string?>
            {
                ["Authentication:EntraExternalId:Enabled"] = "true",
                ["Authentication:EntraExternalId:TenantId"] = "test",
                ["Authentication:EntraExternalId:TenantDomain"] = "test.ciamlogin.com",
                ["Authentication:EntraExternalId:ClientId"] = Audience,
                ["FeatureFlags:UserPersistence"] = persistence.ToString(),
                ["Database:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1",
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["ApiProtection:RateLimiting:Global:PermitLimit"] = globalLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Redis:ConnectionString"] = "",
                ["Logging:EventLog:LogLevel:Default"] = "None",
            }) builder.UseSetting(setting.Key, setting.Value!);
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(ProviderSchemeNames.EntraExternalId, options =>
                {
                    var config = new OpenIdConnectConfiguration { Issuer = Issuer };
                    config.SigningKeys.Add(_key);
                    options.Configuration = config;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(config);
                });
                if (failStar)
                {
                    services.RemoveAll<IAuthorizeFacade>();
                    services.AddSingleton<IAuthorizeFacade, FailingFacade>();
                }
            });
        }

        public string Token(string audience = Audience, string? scope = "access_as_user") => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Subject = new ClaimsIdentity(new[] { new Claim("oid", "shared-user"), new Claim("name", "Shared User"), new Claim("email", "shared@example.com") }.Concat(scope is null ? [] : new[] { new Claim("scp", scope) })),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed class FailingFacade : IAuthorizeFacade
    {
        public Task<UserInfo> SignInUserAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, CancellationToken cancellationToken) =>
            throw new MyVedicStar.Shared.Exceptions.DataAccessException("internal database details");
    }
}