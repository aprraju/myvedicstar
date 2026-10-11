namespace MyVedicStar.Authorize.Business.Users;

/// <summary>The signed-in user as described by their validated access token. Absent values are <see langword="null"/>.</summary>
/// <param name="ExternalUserId">The user's object ID in the identity provider.</param>
/// <param name="Name">Display name.</param>
/// <param name="Email">Email address.</param>
/// <param name="Issuer">Validated token issuer.</param>
/// <param name="SessionKey">Hash of the session identifier; never a token.</param>
internal sealed record SignedInUser(string ExternalUserId, string? Name, string? Email, string Issuer = "", string? SessionKey = null);
