using System.Net;
using System.Security.Claims;

namespace MyVedicStar.Authorize.Business;

/// <summary>
/// Public entry point to the Authorize domain.
/// </summary>
public interface IAuthorizeFacade
{
    /// <summary>
    /// Completes sign-in for an authenticated user: refreshes their stored profile from the identity provider,
    /// records the login, and returns their basic profile.
    /// </summary>
    /// <param name="user">The user whose access token has already been validated by the API.</param>
    /// <param name="clientIpAddress">The client's IP address as resolved by the API, if known.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="Shared.Exceptions.AuthenticationFailedException">The token does not identify a directory user.</exception>
    /// <exception cref="Shared.Exceptions.ResourceNotFoundException">The user no longer exists in the directory.</exception>
    /// <exception cref="Shared.Exceptions.IntegrationException">The identity provider is unavailable.</exception>
    /// <exception cref="Shared.Exceptions.DataAccessException">The profile or login could not be saved.</exception>
    Task<UserInfo> SignInUserAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, CancellationToken cancellationToken);
}
