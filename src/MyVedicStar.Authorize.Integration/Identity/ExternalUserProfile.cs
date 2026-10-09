namespace MyVedicStar.Authorize.Integration.Identity;

/// <summary>A user's profile as held by the external identity provider. Absent values are <see langword="null"/>.</summary>
/// <param name="ExternalUserId">The user's object ID in the identity provider.</param>
/// <param name="Email">The user's email address or email sign-in name.</param>
/// <param name="GivenName">Given (first) name.</param>
/// <param name="Surname">Surname (last name).</param>
public sealed record ExternalUserProfile(
    string ExternalUserId,
    string? Email,
    string? GivenName,
    string? Surname);
