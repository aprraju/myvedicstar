namespace MyVedicStar.Authorize.Data.Users;

/// <summary>The stored account after registration or synchronization.</summary>
/// <param name="Id">Internal MyVedicStar user ID.</param>
/// <param name="DisplayName">Stored display name.</param>
/// <param name="GivenName">Stored first name.</param>
/// <param name="FamilyName">Stored last name.</param>
public sealed record RegisteredUser(Guid Id, string? DisplayName, string? GivenName, string? FamilyName);

/// <summary>Persistence operations for MyVedicStar users.</summary>
public interface IUserRepository
{
    /// <summary>
    /// Creates an account identified by issuer and external ID, or synchronizes its name and email.
    /// Preserves account ID, creation time and active status; rejects inactive accounts. A display name is
    /// never replaced by null, and once the profile is edited the token only fills in a missing display name.
    /// </summary>
    /// <returns>The stored account, including the names the API returns.</returns>
    Task<RegisteredUser> UpsertAsync(UserProfileData profile, CancellationToken cancellationToken);
    Task<Guid?> FindIdAsync(string issuer, string externalId, CancellationToken cancellationToken);
}
