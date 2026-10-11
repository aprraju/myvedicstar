using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyVedicStar.ApiProtection.Abstractions;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.RateLimiting;
using MyVedicStar.API.Contracts;
using MyVedicStar.Authorize.Business;

namespace MyVedicStar.API.Controllers;

/// <summary>Endpoints for the signed-in user.</summary>
[ApiController]
[Authorize(Policy = AuthPolicyNames.ApiScope)]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthorizeFacade _authorizeFacade;
    private readonly IClientIpResolver _clientIpResolver;

    public AuthController(IAuthorizeFacade authorizeFacade, IClientIpResolver clientIpResolver)
    {
        _authorizeFacade = authorizeFacade;
        _clientIpResolver = clientIpResolver;
    }

    /// <summary>
    /// Registers the validated account if necessary, records the session's login once, and returns ID, name and email.
    /// </summary>
    /// <remarks>
    /// Repeated requests in the same session do not create duplicate accounts or audit events.
    /// </remarks>
    /// <response code="200">The user's ID, name and email.</response>
    /// <response code="401">The bearer token is missing, invalid or expired, or does not identify a user.</response>
    /// <response code="403">The token is valid but lacks the API scope (for example an app-only token).</response>
    /// <response code="413">The request body exceeds the allowed size.</response>
    /// <response code="429">Too many requests from this user; retry later.</response>
    /// <response code="503">The database is temporarily unavailable.</response>
    [HttpPost("userinfo")]
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicyNames.AuthenticatedStandard)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<UserInfoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<UserInfoResponse>> SignInUser(CancellationToken cancellationToken,
        [FromHeader(Name = "X-Auth-Session-Id")] string? sessionId = null)
    {
        var userInfo = await _authorizeFacade.LoginAsync(User, _clientIpResolver.Resolve(HttpContext), sessionId, cancellationToken);

        return Ok(new UserInfoResponse(userInfo.Id, userInfo.Name, userInfo.Email));
    }

    /// <summary>Records explicit logout once per session. The client still signs out of its identity provider.</summary>
    [HttpPost("logout")]
    [EnableRateLimiting(RateLimitPolicyNames.AuthenticatedStandard)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken,
        [FromHeader(Name = "X-Auth-Session-Id")] string? sessionId = null)
    {
        await _authorizeFacade.LogoutAsync(User, _clientIpResolver.Resolve(HttpContext), sessionId, cancellationToken);
        return NoContent();
    }
}
