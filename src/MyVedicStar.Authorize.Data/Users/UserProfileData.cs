namespace MyVedicStar.Authorize.Data.Users;

/// <summary>Profile values to store for a user, keyed by their identity provider object ID.</summary>
/// <remarks>
/// Each value must fit its column (<see cref="MaxExternalIdLength"/>, <see cref="MaxEmailLength"/>,
/// <see cref="MaxNameLength"/>, in characters); the database rejects longer values.
/// </remarks>
public sealed record UserProfileData(
    string ExternalId,
    string? Email,
    string? GivenName,
    string? FamilyName,
    string Issuer = "")
{
    public const int MaxIssuerLength = 512;
    /// <summary>Longest storable identity provider object ID.</summary>
    public const int MaxExternalIdLength = 128;

    /// <summary>Longest storable email address (the RFC 5321 limit).</summary>
    public const int MaxEmailLength = 320;

    /// <summary>Longest storable given or family name.</summary>
    public const int MaxNameLength = 256;
}
