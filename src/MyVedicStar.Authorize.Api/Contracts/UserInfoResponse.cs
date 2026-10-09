namespace MyVedicStar.Authorize.Api.Contracts;

/// <summary>Minimal profile of the signed-in user.</summary>
/// <param name="Id">Internal MyVedicStar user ID (the Entra object ID while user persistence is disabled).</param>
/// <param name="GivenName">Given (first) name, or <see langword="null"/> if unknown.</param>
/// <param name="Surname">Surname (last name), or <see langword="null"/> if unknown.</param>
/// <param name="Name">Given name and surname joined with a space, or <see langword="null"/> if both are unknown.</param>
/// <param name="Email">Email address, or <see langword="null"/> if unknown.</param>
public sealed record UserInfoResponse(string Id, string? GivenName, string? Surname, string? Name, string? Email);
