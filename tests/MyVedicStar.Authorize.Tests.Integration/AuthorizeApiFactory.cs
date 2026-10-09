using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.Authorize.Data;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Authorize.Integration.Identity;
using MyVedicStar.Authorize.Tests.Integration.Fakes;

namespace MyVedicStar.Authorize.Tests.Integration;

/// <summary>
/// Hosts the real API pipeline with the Development configuration. The JWT handler trusts a test issuer and
/// signing key; Microsoft Graph and PostgreSQL are replaced by in-memory fakes.
/// </summary>
public class AuthorizeApiFactory : WebApplicationFactory<Program>
{
    public const string TenantId = "test-tenant";
    public const string Issuer = $"https://{TenantId}.ciamlogin.com/{TenantId}/v2.0";
    public const string Audience = "test-api-client-id";
    public const string TrustedProxyIp = "10.0.0.1";

    /// <summary>The delegated scope the API requires (Authorization:RequiredScopes in appsettings.json).</summary>
    public const string ApiScope = "access_as_user";

    /// <summary>Test-only header that sets the TCP peer address, since the test server has no real socket.</summary>
    public const string RemoteIpTestHeader = "X-Test-Remote-Ip";

    private readonly RsaSecurityKey _signingKey = CreateKey("test-tenant-key");

    public FakeExternalIdentityService ExternalIdentityService { get; } = new();

    public InMemoryUserStore UserStore { get; } = new();

    /// <summary>Value of the <c>FeatureFlags:UserPersistence</c> setting for this host.</summary>
    protected virtual bool UserPersistenceEnabled => true;

    /// <summary>
    /// Per-user limit for the <c>authenticated-standard</c> policy; high by default so ordinary tests never hit it.
    /// </summary>
    protected virtual int RateLimitPermitLimit => 10_000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("FeatureFlags:UserPersistence", UserPersistenceEnabled.ToString());
        builder.UseSetting("Authentication:EntraExternalId:TenantDomain", $"{TenantId}.ciamlogin.com");
        builder.UseSetting("Authentication:EntraExternalId:TenantId", TenantId);
        builder.UseSetting("Authentication:EntraExternalId:ClientId", Audience);
        builder.UseSetting("Authentication:EntraExternalId:MetadataAddress", $"{Issuer}/.well-known/openid-configuration");
        builder.UseSetting("Integrations:MicrosoftGraph:TenantId", "test-tenant");
        builder.UseSetting("Integrations:MicrosoftGraph:ClientId", "test-graph-client");
        builder.UseSetting("Integrations:MicrosoftGraph:ClientSecret", "test-graph-secret");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("ApiProtection:TrustedProxies:0", TrustedProxyIp);
        builder.UseSetting("ApiProtection:RateLimiting:AuthenticatedStandard:PermitLimit", RateLimitPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("ApiProtection:RateLimiting:Global:PermitLimit", "10000");

        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(ProviderSchemeNames.EntraExternalId, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(_signingKey);

                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });

            services.RemoveAll<IExternalIdentityService>();
            services.AddSingleton<IExternalIdentityService>(ExternalIdentityService);

            if (UserPersistenceEnabled)
            {
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(UserStore);
                services.RemoveAll<IUserLoginRepository>();
                services.AddSingleton<IUserLoginRepository>(UserStore);
                services.RemoveAll<IAuthorizeUnitOfWork>();
                services.AddSingleton<IAuthorizeUnitOfWork>(UserStore);
            }

            services.AddTransient<IStartupFilter, RemoteIpStartupFilter>();
        });
    }

    public string CreateToken(
        IEnumerable<(string Type, string Value)> claims,
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expiresUtc = null,
        RsaSecurityKey? signingKey = null,
        string? scope = ApiScope)
    {
        var expires = expiresUtc ?? DateTime.UtcNow.AddMinutes(30);

        // User tokens carry the API's delegated scope; pass scope: null to mimic an app-only token.
        if (scope is not null)
        {
            claims = claims.Append(("scp", scope));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = expires.AddHours(-1),
            NotBefore = expires.AddHours(-1),
            Expires = expires,
            Subject = new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value))),
            SigningCredentials = new SigningCredentials(signingKey ?? _signingKey, SecurityAlgorithms.RsaSha256),
        });
    }

    public void ResetFakes()
    {
        ExternalIdentityService.Reset();
        UserStore.Reset();
    }

    public static RsaSecurityKey CreateKey(string keyId) => new(RSA.Create(2048)) { KeyId = keyId };

    /// <summary>Runs before the app's own pipeline so forwarded-header handling sees the simulated peer address.</summary>
    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[RemoteIpTestHeader], out var remoteIp))
                {
                    context.Connection.RemoteIpAddress = remoteIp;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
