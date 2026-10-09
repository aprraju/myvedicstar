using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace MyVedicStar.Authorize.Api.Authentication;

internal static class AuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Authenticates requests using bearer tokens issued by Microsoft Entra External ID.
    /// Signature, issuer, audience and lifetime are validated by the framework's JWT handler.
    /// </summary>
    public static IServiceCollection AddEntraExternalIdAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EntraExternalIdAuthenticationOptions>()
            .Bind(configuration.GetSection(EntraExternalIdAuthenticationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<EntraExternalIdAuthenticationOptions>>((jwtBearer, entraOptions) =>
            {
                var entra = entraOptions.Value;

                jwtBearer.MetadataAddress = entra.MetadataAddress;
                jwtBearer.RequireHttpsMetadata = entra.RequireHttpsMetadata;
                jwtBearer.Audience = entra.Audience;
                jwtBearer.AutomaticRefreshInterval = entra.MetadataAutomaticRefreshInterval;
                jwtBearer.RefreshInterval = entra.MetadataRefreshInterval;
                jwtBearer.BackchannelTimeout = entra.MetadataTimeout;

                // Keep the token's own claim names (oid, email, given_name, ...) so they match configuration.
                jwtBearer.MapInboundClaims = false;

                jwtBearer.TokenValidationParameters.ValidAlgorithms = entra.ValidAlgorithms;
                jwtBearer.TokenValidationParameters.ClockSkew = entra.ClockSkew;
                jwtBearer.TokenValidationParameters.ValidateIssuer = true;
                jwtBearer.TokenValidationParameters.ValidateAudience = true;
                jwtBearer.TokenValidationParameters.ValidateLifetime = true;
                jwtBearer.TokenValidationParameters.ValidateIssuerSigningKey = true;
                jwtBearer.TokenValidationParameters.RequireSignedTokens = true;
                jwtBearer.TokenValidationParameters.RequireExpirationTime = true;
            });

        return services;
    }
}
