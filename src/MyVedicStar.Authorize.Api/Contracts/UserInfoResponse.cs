namespace MyVedicStar.API.Contracts;

/// <summary>Minimal profile of the signed-in user.</summary>
/// <param name="Id">The user's Entra object ID from the access token.</param>
/// <param name="Name">The stored display name, or <see langword="null"/> if none is known. Not built from first and last name.</param>
/// <param name="Email">Email address from the access token, or <see langword="null"/> if the token has none.</param>
/// <param name="FirstName">The stored first name, or <see langword="null"/>.</param>
/// <param name="LastName">The stored last name, or <see langword="null"/>.</param>
public sealed record UserInfoResponse(string Id, string? Name, string? Email, string? FirstName, string? LastName);
