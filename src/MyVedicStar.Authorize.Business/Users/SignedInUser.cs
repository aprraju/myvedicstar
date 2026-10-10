namespace MyVedicStar.Authorize.Business.Users;

/// <summary>The signed-in user as described by their validated access token. Absent values are <see langword="null"/>.</summary>
/// <param name="ExternalUserId">The user's object ID in the identity provider.</param>
/// <param name="Name">Display name.</param>
/// <param name="Email">Email address.</param>
internal sealed record SignedInUser(string ExternalUserId, string? Name, string? Email);
