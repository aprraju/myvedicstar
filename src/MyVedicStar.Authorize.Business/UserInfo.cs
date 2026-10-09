namespace MyVedicStar.Authorize.Business;

/// <summary>Minimal profile of the signed-in user.</summary>
/// <param name="Id">
/// Internal MyVedicStar user ID. While the user-persistence feature flag is off, this is the identity provider's object ID.
/// </param>
/// <param name="GivenName">Given (first) name, if known.</param>
/// <param name="Surname">Surname (last name), if known.</param>
/// <param name="Name">Display name built from the given name and surname, or <see langword="null"/> if both are missing.</param>
/// <param name="Email">Email address, if known.</param>
public sealed record UserInfo(string Id, string? GivenName, string? Surname, string? Name, string? Email);
