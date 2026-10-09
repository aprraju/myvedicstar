using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyVedicStar.Authorize.Api.Contracts;
using MyVedicStar.Authorize.Api.Networking;
using MyVedicStar.Authorize.Business;

namespace MyVedicStar.Authorize.Api.Controllers;

/// <summary>Endpoints for the signed-in user.</summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthorizeFacade _authorizeFacade;

    public AuthController(IAuthorizeFacade authorizeFacade)
    {
        _authorizeFacade = authorizeFacade;
    }

    /// <summary>
    /// Completes sign-in for the bearer of an Entra External ID access token: refreshes the user's stored profile
    /// from Microsoft Graph, records the login, and returns the user's basic profile.
    /// </summary>
    /// <remarks>
    /// This is a POST because every call creates or updates the user record and writes a login audit entry.
    /// </remarks>
    /// <response code="200">The user's profile.</response>
    /// <response code="401">The bearer token is missing, invalid or expired, or does not identify a user.</response>
    /// <response code="404">The user no longer exists in the directory.</response>
    /// <response code="503">The identity provider or database is temporarily unavailable.</response>
    [HttpPost("userinfo")]
    [Authorize]
    [ProducesResponseType<UserInfoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<UserInfoResponse>> SignInUser(CancellationToken cancellationToken)
    {
        var userInfo = await _authorizeFacade.SignInUserAsync(User, HttpContext.GetClientIpAddress(), cancellationToken);

        return Ok(new UserInfoResponse(
            userInfo.Id,
            userInfo.GivenName,
            userInfo.Surname,
            userInfo.Name,
            userInfo.Email));
    }
}
