using Microsoft.AspNetCore.Authorization;

namespace MyVedicStar.Authorize.Api.Authorization;

/// <summary>The token must carry one of the configured delegated scopes (<see cref="ApiAuthorizationOptions"/>).</summary>
internal sealed class ScopeRequirement : IAuthorizationRequirement;
