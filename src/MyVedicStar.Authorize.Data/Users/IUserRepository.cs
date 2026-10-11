namespace MyVedicStar.Authorize.Data.Users;

/// <summary>Persistence operations for MyVedicStar users.</summary>
public interface IUserRepository
{
    /// <summary>
    /// Creates an account identified by issuer and external ID, or synchronizes its name and email.
    /// Preserves account ID, creation time and active status; rejects inactive accounts.
    /// </summary>
    /// <returns>The internal MyVedicStar user ID.</returns>
    Task<Guid> UpsertAsync(UserProfileData profile, CancellationToken cancellationToken);
    Task<Guid?> FindIdAsync(string issuer, string externalId, CancellationToken cancellationToken);
}
