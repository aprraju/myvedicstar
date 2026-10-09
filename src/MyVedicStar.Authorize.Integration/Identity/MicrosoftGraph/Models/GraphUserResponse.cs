namespace MyVedicStar.Authorize.Integration.Identity.MicrosoftGraph.Models;

/// <summary>The subset of the Graph <c>user</c> resource this adapter selects.</summary>
internal sealed record GraphUserResponse(
    string? Id,
    string? Mail,
    IReadOnlyList<string>? OtherMails,
    IReadOnlyList<GraphObjectIdentity>? Identities,
    string? GivenName,
    string? Surname);

/// <summary>A sign-in identity on a Graph user (for example a local email account).</summary>
internal sealed record GraphObjectIdentity(string? SignInType, string? Issuer, string? IssuerAssignedId);
