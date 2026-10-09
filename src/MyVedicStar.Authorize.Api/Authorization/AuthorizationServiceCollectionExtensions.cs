using Microsoft.AspNetCore.Authorization;

namespace MyVedicStar.Authorize.Api.Authorization;

internal static class AuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="AuthorizationPolicies.SignedInUser"/> policy and makes it both the default policy
    /// (used by a bare <c>[Authorize]</c>) and the fallback policy (used by any endpoint without its own), so nothing
    /// is reachable without a scoped user token unless it explicitly allows anonymous access.
    /// </summary>
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiAuthorizationOptions>()
            .Bind(configuration.GetSection(ApiAuthorizationOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.RequiredScopes.All(scope => !string.IsNullOrWhiteSpace(scope) && !scope.Contains(' ')),
                $"{ApiAuthorizationOptions.SectionName}:{nameof(ApiAuthorizationOptions.RequiredScopes)} entries must be non-empty scope names without spaces.")
            .ValidateOnStart();

        services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

        var signedInUser = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new ScopeRequirement())
            .Build();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.SignedInUser, signedInUser)
            .SetDefaultPolicy(signedInUser)
            .SetFallbackPolicy(signedInUser);

        return services;
    }
}
