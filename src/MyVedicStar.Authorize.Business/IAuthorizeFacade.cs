using System.Net;
using System.Security.Claims;

namespace MyVedicStar.Authorize.Business;

/// <summary>
/// Public entry point to the Authorize domain.
/// </summary>
public interface IAuthorizeFacade
{
    /// <summary>
    /// Registers an authenticated account if necessary, audits the session once,
    /// and returns the existing ID, name and email response from the token.
    /// </summary>
    /// <param name="user">The user whose access token has already been validated by the API.</param>
    /// <param name="clientIpAddress">The client's IP address as resolved by the API, if known.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="Shared.Exceptions.AuthenticationFailedException">The token does not identify a user and issuer.</exception>
    /// <exception cref="Shared.Exceptions.DataAccessException">The profile or login could not be saved.</exception>
    Task<UserInfo> SignInUserAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, CancellationToken cancellationToken);
    /// <summary>Completes login, optionally using a client session UUID for idempotency across token refreshes.</summary>
    Task<UserInfo> LoginAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, string? sessionId, CancellationToken cancellationToken);
    /// <summary>Audits explicit logout once per session, without changing provider authentication.</summary>
    /// <summary>Reads the authenticated account without creating a user or audit event.</summary>
    Task<ProfileResult> GetProfileAsync(ClaimsPrincipal user, CancellationToken token);
    /// <summary>Updates only supplied fields and attempts pending name synchronization.</summary>
    Task<ProfileResult> UpdateProfileAsync(ClaimsPrincipal user, ProfileUpdateRequest request, CancellationToken token);
    /// <summary>Retries durable pending name synchronization.</summary>
    Task<ProfileResult> RetryProfileSyncAsync(ClaimsPrincipal user, CancellationToken token);
    /// <summary>Returns the active registered account ID derived from verified token identity.</summary>
    Task<Guid> GetRegisteredAccountIdAsync(ClaimsPrincipal user, CancellationToken token);
    Task LogoutAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, string? sessionId, CancellationToken cancellationToken);
}
