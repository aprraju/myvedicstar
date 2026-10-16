namespace MyVedicStar.Authorize.Business.Users;

/// <summary>The signed-in user as described by their validated access token. Absent values are <see langword="null"/>.</summary>
/// <param name="ExternalUserId">The user's object ID in the identity provider.</param>
/// <param name="GivenName">Given name.</param>
/// <param name="FamilyName">Family name.</param>
/// <param name="Email">Email address.</param>
/// <param name="Issuer">Validated token issuer.</param>
/// <param name="SessionKey">Hash of the session identifier; never a token.</param>
/// <param name="DisplayName">Display name (the configured name claim, normally <c>name</c>).</param>
internal sealed record SignedInUser(string ExternalUserId, string? GivenName, string? FamilyName, string? Email, string Issuer = "",
    string? SessionKey = null, string? DisplayName = null);
