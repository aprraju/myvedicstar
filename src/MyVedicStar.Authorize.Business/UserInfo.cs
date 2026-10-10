namespace MyVedicStar.Authorize.Business;

/// <summary>Minimal profile of the signed-in user, taken from their validated access token.</summary>
/// <param name="Id">The user's object ID in the identity provider (the token's user ID claim).</param>
/// <param name="Name">Display name, if the token carries one.</param>
/// <param name="Email">Email address, if the token carries one.</param>
public sealed record UserInfo(string Id, string? Name, string? Email);
