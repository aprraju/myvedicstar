extern alias SharedHost;

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;
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

public sealed partial class SharedHostTests
{
    [Fact]
    public async Task GeoNames_RejectedAccount_ReturnsActionableProblemDetails()
    {
        await using var factory = new CombinedFactory(rejectGeoNames: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        var response = await client.GetAsync("/api/v1/locations/search?text=Austin");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("enable web services", body.RootElement.GetProperty("detail").GetString());
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
        Assert.True(body.RootElement.TryGetProperty("correlationId", out _));
        Assert.DoesNotContain("user does not exist", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GeoNames_UsesStarConfigurationWithoutChangingDayDefaults()
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeoNames.GeoNamesOptions>>().Value;
        Assert.True(options.Enabled);
        Assert.Equal("aprraju", options.Username);
    }

    [Fact]
    public async Task ConsolidatedApi_PreservesEveryControllerRouteAndRequiresAuthentication()
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>() is not null)
            .ToArray();
        string[] expected =
        [
            "POST api/email/send",
            "POST api/v1/email/send",
            "POST api/v1/auth/userinfo",
            "POST api/v1/auth/login",
            "POST api/v1/auth/logout",
            "GET api/v1/auth/profile",
            "PATCH api/v1/auth/profile",
            "POST api/v1/auth/profile/sync",
            "GET api/v1/settings",
            "PUT api/v1/settings",
            "GET api/v1/settings/locations",
            "POST api/v1/settings/locations",
            "DELETE api/v1/settings/locations/{locationId:guid}",
            "GET api/v1/locations/countries",
            "GET api/v1/locations/regions",
            "GET api/v1/locations/address-cities",
            "GET api/v1/festivals/upcoming",
            "GET api/v1/locations/resolve",
            "GET api/v1/locations/search",
            "GET api/v1/locations/postal",
            "GET api/v1/locations/reverse",
            "GET api/v1/locations/timezone",
            "GET api/v1/muhurta/nakshatras",
            "GET api/v1/muhurta/activities",
            "GET api/v1/muhurta/good-times",
            "GET api/v1/muhurta/good-times/explanation",
            "GET api/v1/muhurta/day-summaries",
            "GET api/v1/muhurta/good-times/best",
            "GET api/v1/panchang/today",
            "GET api/v1/panchang/tomorrow",
            "GET api/v1/panchang/ten-days",
            "GET api/v1/panchang/range",
            "GET api/v1/panchang/month/{year:int}/{month:int}",
            "GET api/v1/panchang/year/{year:int}",
            "GET api/v1/panchang/{date}",
            "GET api/v1/panchang/glossary",
            "GET api/v1/panchang/traditions",
            "GET api/v1/traditions",
            "GET api/v1/vedic-profiles",
            "POST api/v1/vedic-profiles",
            "GET api/v1/vedic-profiles/{id:guid}",
            "PUT api/v1/vedic-profiles/{id:guid}",
            "DELETE api/v1/vedic-profiles/{id:guid}",
            "GET api/v1/kundali/capabilities",
            "GET api/v1/kundali/{profileId:guid}",
            "GET api/v1/kundali/{profileId:guid}/overview",
            "POST api/v1/kundali/{profileId:guid}/generations",
            "POST api/v1/kundali/{profileId:guid}/retry",
            "GET api/v1/kundali/{profileId:guid}/sections/dashas",
            "GET api/v1/kundali/{profileId:guid}/preferences",
            "PUT api/v1/kundali/{profileId:guid}/preferences",
        ];
        var actual = endpoints.SelectMany(endpoint => endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods
            .Select(method => $"{method} {endpoint.RoutePattern.RawText}"));
        Assert.Equal(expected.Order(), actual.Order());
        var controllerAssemblies = new Dictionary<string, string>
        {
            ["AuthController"] = "MyVedicStar.Authorize.Api",
            ["GeoNamesLocationsController"] = "MyVedicStar.Authorize.Api",
            ["SettingsController"] = "MyVedicStar.Settings.Api",
            ["EmailController"] = "MyVedicStar.Email.Api",
            ["LocationsController"] = "MyVedicDay.Location.Api",
            ["TraditionsController"] = "MyVedicDay.Traditions.Api",
            ["PanchangController"] = "MyVedicDay.Panchang.Api",
            ["PanchangTraditionsController"] = "MyVedicDay.Panchang.Api",
            ["GlossaryController"] = "MyVedicDay.Panchang.Api",
            ["FestivalsController"] = "MyVedicDay.Festivals.Api",
            ["MuhurtaController"] = "MyVedicDay.Muhurta.Api",
            ["VedicProfilesController"] = "MyVedicStar.VedicProfiles.Api",
            ["KundaliController"] = "MyVedicStar.Kundali.Api",
        };
        Assert.All(endpoints, endpoint =>
        {
            var controller = endpoint.Metadata.GetRequiredMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>().ControllerTypeInfo;
            Assert.Equal(controllerAssemblies[controller.Name], controller.Assembly.GetName().Name);
        });
        Assert.All(endpoints.Where(endpoint => endpoint.RoutePattern.RawText is "api/email/send" or "api/v1/email/send"),
            endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>()));
        var protectedEndpoints = endpoints.Where(endpoint => endpoint.RoutePattern.RawText is not ("api/email/send" or "api/v1/email/send"));
        Assert.DoesNotContain(protectedEndpoints, endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null);
        Assert.All(protectedEndpoints, endpoint => Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()));
    }

    [Fact]
    public async Task BothApis_AcceptTheSameAudienceAndPreserveTheirRoutes()
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        var star = await client.PostAsync("/api/v1/auth/userinfo", null);
        Assert.Equal(HttpStatusCode.OK, star.StatusCode);
        using var body = JsonDocument.Parse(await star.Content.ReadAsStringAsync());
        Assert.Equal(["id", "name", "email", "firstName", "lastName"], body.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("shared-user", body.RootElement.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/muhurta/activities")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/muhurta/good-times?latitude=17.385&longitude=78.4867")).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/userinfo", true)]
    [InlineData("/api/v1/panchang/today", false)]
    [InlineData("/api/v1/locations/resolve", false)]
    [InlineData("/api/v1/locations/search?text=Austin", false)]
    [InlineData("/api/v1/locations/postal?text=78701", false)]
    [InlineData("/api/v1/locations/reverse?latitude=30&longitude=-97", false)]
    [InlineData("/api/v1/locations/timezone?latitude=30&longitude=-97", false)]
    [InlineData("/api/v1/locations/countries", false)]
    [InlineData("/api/v1/locations/regions?country=US", false)]
    [InlineData("/api/v1/locations/address-cities?text=Austin&country=US", false)]
    [InlineData("/api/v1/auth/profile", false)]
    [InlineData("/api/v1/settings", false)]
    [InlineData("/api/v1/settings/locations", false)]
    [InlineData("/api/v1/muhurta/good-times?latitude=17.385&longitude=78.4867", false)]
    public async Task ProtectedRoutes_RejectAnonymousRequests(string path, bool post)
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        var response = post ? await client.PostAsync(path, null) : await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong-audience")]
    [InlineData("1a3384a0-8857-4854-8d96-3f94c1b1c18e")]
    [InlineData("8a04afd8-56e8-49a1-b904-7c8f9e7b0df2")]
    public async Task WrongAudience_IsRejectedByBothApis(string audience)
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(audience));
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
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
        Assert.Equal("same-origin", Assert.Single(response.Headers.GetValues("Cross-Origin-Opener-Policy")));
        Assert.Equal("shared-host-error", Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
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

    private sealed class CombinedFactory(bool failStar = false, int globalLimit = 1000, bool persistence = false, bool rejectGeoNames = false) : WebApplicationFactory<HostProgram>
    {
        private const string Issuer = "https://cbece845-e124-4de7-89d3-96a71abb7b83.ciamlogin.com/cbece845-e124-4de7-89d3-96a71abb7b83/v2.0";
        private const string Audience = "fdaa908a-4c0b-4d63-a6d1-de637e0edc4e";
        private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "shared-key" };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            foreach (var setting in new Dictionary<string, string?>
            {
                ["Authentication:EntraExternalId:Enabled"] = "true",
                ["Authentication:EntraExternalId:TenantId"] = "cbece845-e124-4de7-89d3-96a71abb7b83",
                ["Authentication:EntraExternalId:TenantDomain"] = "cbece845-e124-4de7-89d3-96a71abb7b83.ciamlogin.com",
                ["Authentication:EntraExternalId:ClientId"] = Audience,
                ["FeatureFlags:UserPersistence"] = persistence.ToString(),
                ["Database:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1",
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["ApiProtection:RateLimiting:Global:PermitLimit"] = globalLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Redis:ConnectionString"] = "",
                ["Logging:EventLog:LogLevel:Default"] = "None",
                // Development enables email; tests must never reach a real mailbox through local user secrets.
                ["EmailSettings:Enabled"] = "false",
                // Development enables the PostgreSQL (L2) cache; these tests have no cache database.
                ["Caching:L2:Enabled"] = "false",
            }) builder.UseSetting(setting.Key, setting.Value!);
            builder.ConfigureServices(services =>
            {
                if (rejectGeoNames)
                    services.AddHttpClient("myvedicstar-geonames").ConfigurePrimaryHttpMessageHandler(() => new RejectedGeoNamesHandler());
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
            Subject = new ClaimsIdentity(new[] { new Claim("oid", "shared-user"), new Claim("given_name", "Shared"), new Claim("family_name", "User"), new Claim("azp", "8a04afd8-56e8-49a1-b904-7c8f9e7b0df2"), new Claim("sid", "shared-session"), new Claim("email", "shared@example.com") }.Concat(scope is null ? [] : new[] { new Claim("scp", scope) })),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed class FailingFacade : IAuthorizeFacade
    {
        public Task<ProfileResult> GetProfileAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken token) => throw new NotSupportedException();
        public Task<ProfileResult> UpdateProfileAsync(System.Security.Claims.ClaimsPrincipal user, ProfileUpdateRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<ProfileResult> RetryProfileSyncAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken token) => throw new NotSupportedException();
        public Task<Guid> GetRegisteredAccountIdAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken token) => throw new NotSupportedException();
        public Task<UserInfo> LoginAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, string? sessionId, CancellationToken cancellationToken) =>
            SignInUserAsync(user, clientIpAddress, cancellationToken);
        public Task LogoutAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, string? sessionId, CancellationToken cancellationToken) =>
            throw new MyVedicStar.Shared.Exceptions.DataAccessException("internal database details");
        public Task<UserInfo> SignInUserAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, CancellationToken cancellationToken) =>
            throw new MyVedicStar.Shared.Exceptions.DataAccessException("internal database details");
    }

    private sealed class RejectedGeoNamesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"status\":{\"message\":\"user does not exist\",\"value\":10}}")
            });
    }
}
