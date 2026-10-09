using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Api.Authorization;

/// <summary>Delegated permissions (scopes) a caller's access token must carry. Validated at startup.</summary>
public sealed class ApiAuthorizationOptions
{
    public const string SectionName = "Authorization";

    /// <summary>
    /// Scopes this API exposes to client apps (Entra app registration → Expose an API), for example
    /// <c>access_as_user</c>. A token is accepted when it carries at least one of them.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string[] RequiredScopes { get; set; } = [];

    /// <summary>Token claim holding the space-separated delegated scopes: <c>scp</c> in Entra access tokens.</summary>
    [Required]
    public string ScopeClaimType { get; set; } = string.Empty;
}
