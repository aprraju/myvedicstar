using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace MyVedicStar.Authorize.Api.Authorization;

/// <summary>
/// Succeeds when the validated token carries one of the required delegated scopes. App-only (client credentials)
/// tokens have no delegated scopes, so they never satisfy this requirement.
/// </summary>
internal sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    private readonly ApiAuthorizationOptions _options;
    private readonly ILogger<ScopeAuthorizationHandler> _logger;

    public ScopeAuthorizationHandler(IOptions<ApiAuthorizationOptions> options, ILogger<ScopeAuthorizationHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var grantedScopes = context.User.FindAll(_options.ScopeClaimType)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (grantedScopes.Any(scope => _options.RequiredScopes.Contains(scope, StringComparer.Ordinal)))
        {
            context.Succeed(requirement);
        }
        else
        {
            // Scope names only; never the token or its other claims.
            _logger.LogWarning(
                "Access denied: token carries none of the required scopes ({RequiredScopes})",
                string.Join(", ", _options.RequiredScopes));
        }

        return Task.CompletedTask;
    }
}
