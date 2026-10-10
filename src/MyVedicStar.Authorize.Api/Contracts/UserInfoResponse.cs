namespace MyVedicStar.Authorize.Api.Contracts;

/// <summary>Minimal profile of the signed-in user.</summary>
/// <param name="Id">The user's Entra object ID from the access token.</param>
/// <param name="Name">Display name from the access token, or <see langword="null"/> if the token has none.</param>
/// <param name="Email">Email address from the access token, or <see langword="null"/> if the token has none.</param>
public sealed record UserInfoResponse(string Id, string? Name, string? Email);
