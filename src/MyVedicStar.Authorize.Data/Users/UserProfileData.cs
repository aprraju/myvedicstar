namespace MyVedicStar.Authorize.Data.Users;

/// <summary>Profile values to store for a user, keyed by their identity provider object ID.</summary>
public sealed record UserProfileData(
    string ExternalId,
    string? Email,
    string? Name);
