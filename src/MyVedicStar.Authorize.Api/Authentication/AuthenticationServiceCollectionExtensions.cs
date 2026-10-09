using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.Auth.Configuration;
using MyVedicStar.ApiProtection.Auth.Extensions;

namespace MyVedicStar.Authorize.Api.Authentication;

internal static class AuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Multi-scheme bearer authentication from MyVedicStar.ApiProtection: each token is routed by its issuer to the
    /// Entra External ID, Google, Apple or Facebook scheme enabled under the <c>Authentication</c> section.
    /// The Entra scheme is additionally hardened from <see cref="EntraTokenValidationOptions"/>.
    /// </summary>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMyVedicStarAuthentication(configuration);

        // Fail fast on settings the package does not validate. Entra External ID always issues tokens with
        // iss = https://{tenantId}.ciamlogin.com/{tenantId}/v2.0 (even when users sign in on a custom subdomain),
        // and the package derives both the expected issuer and the issuer routing from TenantDomain.
        services.AddOptions<MultiProviderAuthOptions>()
            .Validate(
                options => options.EntraExternalId is not { Enabled: true } entra
                    || (!string.IsNullOrWhiteSpace(entra.TenantId)
                        && !string.IsNullOrWhiteSpace(entra.ClientId)
                        && string.Equals(entra.TenantDomain, $"{entra.TenantId}.ciamlogin.com", StringComparison.OrdinalIgnoreCase)),
                "Authentication:EntraExternalId requires TenantId and ClientId, and TenantDomain must be '{TenantId}.ciamlogin.com' (the host in the token issuer).");

        services.AddOptions<EntraTokenValidationOptions>()
            .Bind(configuration.GetSection(EntraTokenValidationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<JwtBearerOptions>(ProviderSchemeNames.EntraExternalId)
            .PostConfigure<IOptions<EntraTokenValidationOptions>>((jwtBearer, validation) =>
            {
                var entra = validation.Value;

                jwtBearer.AutomaticRefreshInterval = entra.MetadataAutomaticRefreshInterval;
                jwtBearer.RefreshInterval = entra.MetadataRefreshInterval;
                jwtBearer.BackchannelTimeout = entra.MetadataTimeout;

                jwtBearer.TokenValidationParameters.ValidAlgorithms = entra.ValidAlgorithms;
                jwtBearer.TokenValidationParameters.ClockSkew = entra.ClockSkew;
                jwtBearer.TokenValidationParameters.RequireSignedTokens = true;
                jwtBearer.TokenValidationParameters.RequireExpirationTime = true;
            });

        return services;
    }
}
