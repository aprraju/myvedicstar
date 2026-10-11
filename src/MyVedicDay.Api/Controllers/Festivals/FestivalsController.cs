using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyVedicDay.Api.Contracts.Festivals;
using MyVedicDay.Api.Infrastructure.Localization;
using MyVedicDay.Festivals.Business;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.RateLimiting;

namespace MyVedicDay.Api.Controllers.Festivals;

/// <summary>
/// Hindu festivals and recurring observances at a location, for a regional tradition.
/// </summary>
[ApiController]
[Authorize(AuthPolicyNames.AnyProvider)]
[EnableRateLimiting(RateLimitPolicyNames.PublicRead)]
[Route("api/v1/festivals")]
[Tags("Festivals")]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json",
    Description = "Invalid parameters, unknown time zone or tradition, days out of range, or no matching city.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json",
    Description = "Festivals are undefined for this location (no sunrise or sunset during polar day or night).")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json",
    Description = "Rate limit exceeded for this client. Retry after the current window.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json",
    Description = "Unexpected error. Quote the traceId or correlationId when reporting it.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json",
    Description = "A dependency is temporarily unavailable. Retry later.")]
public sealed class FestivalsController(IFestivalFacade festivals) : ControllerBase
{
    private const string Json = "application/json";

    /// <summary>Upcoming festivals at the location.</summary>
    /// <remarks>
    /// Lists the festivals and observances of the selected tradition from today at the location (local date) through
    /// the next `days` − 1 days (default 30). Dates depend on the location's sunrise and sunset, so the same festival can
    /// fall on different dates in India and the USA.
    ///
    /// `tradition` selects which festivals are listed and their regional names: `telugu` lists Ugadi and Vinayaka
    /// Chavithi, `tamil` lists Puthandu and Thai Pongal, `north-indian` lists Karwa Chauth and Holi; `all` lists every
    /// festival. Recurring observances (Ekadashi, Pradosham, Purnima…) have category `observance`.
    ///
    /// Examples: `?place=Hyderabad, Telangana&amp;tradition=telugu`, `?latitude=40.71&amp;longitude=-74.01&amp;days=60`.
    /// </remarks>
    /// <param name="request">Location, tradition, language and number of days.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("upcoming", Name = "GetUpcomingFestivals")]
    [ProducesResponseType<FestivalsResponse>(StatusCodes.Status200OK, Json, Description = "Festivals from local today, in date order.")]
    public async Task<FestivalsResponse> GetUpcomingAsync([FromQuery] FestivalsRequest request, CancellationToken cancellationToken)
    {
        var result = await festivals.GetUpcomingAsync(
            request.ToQuery(LanguagePreferences.FromRequest(Request, request.Language)), request.Days, cancellationToken);

        LanguagePreferences.SetResponseLanguage(Response, result.Language);
        return result.ToResponse();
    }
}
