namespace MyVedicStar.Authorize.Business;

/// <summary>Minimal profile of the signed-in user, taken from their validated access token.</summary>
/// <param name="Id">The user's object ID in the identity provider (the token's user ID claim).</param>
/// <param name="Name">Given name and family name joined with a comma; null when both are absent.</param>
/// <param name="Email">Email address, if the token carries one.</param>
public sealed record UserInfo(string Id, string? Name, string? Email);
