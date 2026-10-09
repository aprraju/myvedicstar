namespace MyVedicStar.Authorize.Data.Users;

/// <summary>Persistence operations for MyVedicStar users.</summary>
public interface IUserRepository
{
    /// <summary>
    /// Creates the user if no user has <see cref="UserProfileData.ExternalId"/>, otherwise overwrites the stored
    /// profile with <paramref name="profile"/>. Safe to repeat and safe under concurrent calls for the same user.
    /// </summary>
    /// <returns>The internal MyVedicStar user ID.</returns>
    Task<Guid> UpsertAsync(UserProfileData profile, CancellationToken cancellationToken);
}
