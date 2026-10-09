namespace MyVedicStar.Authorize.Integration.Identity;

/// <summary>
/// Reads user profiles from the external identity provider's directory.
/// </summary>
public interface IExternalIdentityService
{
    /// <summary>
    /// Retrieves the profile of the user with the given directory object ID.
    /// </summary>
    /// <param name="externalUserId">The user's object ID in the identity provider.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="Shared.Exceptions.ResourceNotFoundException">The user does not exist in the directory.</exception>
    /// <exception cref="Shared.Exceptions.IntegrationException">The directory could not be reached or rejected the request.</exception>
    Task<ExternalUserProfile> GetUserProfileAsync(string externalUserId, CancellationToken cancellationToken);
}
