using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyVedicDay.Api.Contracts.Locations;
using MyVedicDay.Location;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.RateLimiting;

namespace MyVedicDay.Api.Controllers.Locations;

/// <summary>
/// Location resolution: confirms a typed place, or names the browser's position, before the calendar is loaded.
/// </summary>
[ApiController]
[Authorize(AuthPolicyNames.AnyProvider)]
[EnableRateLimiting(RateLimitPolicyNames.PublicRead)]
[Route("api/v1/locations")]
[Tags("Locations")]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json",
    Description = "Missing or invalid location, or no matching city.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json",
    Description = "Rate limit exceeded for this client. Retry after the current window.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json",
    Description = "Unexpected error. Quote the traceId or correlationId when reporting it.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json",
    Description = "A dependency is temporarily unavailable. Retry later.")]
public sealed class LocationsController(ILocationFacade locations) : ControllerBase
{
    /// <summary>Resolve a place or name the browser's position.</summary>
    /// <remarks>
    /// Returns the coordinates, time zone and city that the Panchang endpoints would use for the same parameters,
    /// without calculating a Panchang. Typical uses:
    ///
    /// * **Confirm a typed place:** `?place=Austin, TX` → "Austin, Texas, United States", `America/Chicago`.
    /// * **Disambiguate with the browser position:** `?place=Hyderabad&amp;latitude=24.86&amp;longitude=67.01`
    ///   → Hyderabad, Sindh, Pakistan (the country comes from the browser position).
    /// * **Label the browser position:** `?latitude=17.385&amp;longitude=78.4867` → "Hyderabad, Telangana, India".
    ///   `place` is `null` when no city is within 50 km.
    ///
    /// `source` is `Place` for a place search and `Coordinates` when browser coordinates were used directly.
    /// </remarks>
    /// <param name="location">A place and/or browser coordinates.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("resolve", Name = "ResolveLocation")]
    [AllowAnonymous]
    [ProducesResponseType<LocationResponse>(StatusCodes.Status200OK, "application/json",
        Description = "The resolved coordinates, time zone and matched or nearest city.")]
    public async Task<LocationResponse> ResolveAsync([FromQuery] LocationRequest location, CancellationToken cancellationToken) =>
        (await locations.ResolveAsync(location.ToLocationQuery(), cancellationToken)).ToResponse();
}
