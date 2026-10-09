namespace MyVedicStar.Authorize.Api.Authorization;

/// <summary>Names of the authorization policies used by this API.</summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// A user signed in to MyVedicStar: a validated Entra External ID access token issued to a user (not an app)
    /// that carries one of <see cref="ApiAuthorizationOptions.RequiredScopes"/>. Applied to every controller.
    /// </summary>
    public const string SignedInUser = nameof(SignedInUser);
}
