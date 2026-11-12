using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyVedicDay.Api.Contracts.Muhurta;
using MyVedicDay.Api.Infrastructure.Localization;
using MyVedicDay.Muhurta.Business;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.RateLimiting;
using MyVedicStar.ApiProtection.Abstractions;
using MyVedicStar.ApiProtection.Auth.Claims;
using MyVedicStar.ApiProtection.Resilience;
using System.Security.Claims;

namespace MyVedicDay.Api.Controllers.Muhurta;

/// <summary>
/// Find a Good Time: activity-specific Muhurta guidance with conflicts between favorable and unfavorable periods
/// resolved by the backend rules engine.
/// </summary>
[ApiController]
[Authorize(AuthPolicyNames.AnyProvider)]
[EnableRateLimiting(RateLimitPolicyNames.GoodTime)]
[Route("api/v1/muhurta")]
[Tags("Find a Good Time")]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json",
    Description = "Invalid parameters, unknown activity, tradition or time zone, missing or invalid otherText, or date out of range.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json",
    Description = "Good times are undefined for this location and date (no sunrise or sunset during polar day or night).")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json",
    Description = "Rate limit exceeded for this client. Retry after the current window.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json",
    Description = "Unexpected error. Quote the traceId or correlationId when reporting it.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json",
    Description = "A dependency is temporarily unavailable. Retry later.")]
public sealed class MuhurtaController(
    IMuhurtaFacade muhurta,
    IConcurrencyProtectionService concurrency,
    IClientIdentityResolver identityResolver,
    ICalculationTimeoutService timeouts) : ControllerBase
{
    private const string Json = "application/json";

    /// <summary>Birth Nakshatras for the Find a Good Time picker.</summary>
    /// <remarks>Returns all 27 stars from Ashwini to Revati. Display the localized name and pass the canonical key as nakshatra to Find a Good Time endpoints. Abhijit is not included.</remarks>
    /// <param name="tradition">Optional tradition for regional names.</param>
    /// <param name="language">Optional response language (BCP 47).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("nakshatras", Name = "GetMuhurtaNakshatras")]
    [EnableRateLimiting(RateLimitPolicyNames.PublicRead)]
    [ProducesResponseType<MuhurtaNakshatrasResponse>(StatusCodes.Status200OK, Json, Description = "The 27 birth stars with canonical keys and localized names.")]
    public async Task<MuhurtaNakshatrasResponse> GetNakshatrasAsync(
        [FromQuery, RegularExpression("^[A-Za-z][A-Za-z0-9-]{0,31}$")] string? tradition,
        [FromQuery, RegularExpression(LanguagePreferences.LanguageTagPattern)] string? language,
        CancellationToken cancellationToken)
    {
        var languages = LanguagePreferences.FromRequest(Request, language);
        var result = await muhurta.GetNakshatrasAsync(tradition, languages, cancellationToken);
        LanguagePreferences.SetResponseLanguage(Response, result.Language);
        return new(result.Language, result.Nakshatras.Select(star => new MuhurtaNakshatraResponse(star.Key, star.Name, star.Order)).ToArray());
    }

    /// <summary>Activities for "What are you planning?".</summary>
    /// <remarks>
    /// The activity cards, in display order, with icon keys. Render the cards from this list so new activities need no
    /// client release. `general` is the default; `other` requires a description (`otherText`).
    /// </remarks>
    /// <param name="tradition">Optional tradition, for regional names.</param>
    /// <param name="language">Optional response language (BCP 47).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("activities", Name = "GetMuhurtaActivities")]
    [ProducesResponseType<MuhurtaActivitiesResponse>(StatusCodes.Status200OK, Json, Description = "The activities in display order.")]
    public async Task<MuhurtaActivitiesResponse> GetActivitiesAsync(
        [FromQuery, RegularExpression("^[A-Za-z][A-Za-z0-9-]{0,31}$")] string? tradition,
        [FromQuery, RegularExpression(LanguagePreferences.LanguageTagPattern)] string? language,
        CancellationToken cancellationToken)
    {
        var result = await muhurta.GetActivitiesAsync(tradition, LanguagePreferences.FromRequest(Request, language), cancellationToken);
        LanguagePreferences.SetResponseLanguage(Response, result.Language);
        return result.ToResponse();
    }

    /// <summary>Good times for an activity on a date.</summary>
    /// <remarks>
    /// Evaluates the day at the location for the activity and tradition. The day is cut wherever an input changes
    /// (every Muhurta period boundary and every Tithi, Nakshatra, Yoga and Karana change), and each segment gets a
    /// resolved `status` with `reasons` and `conflicts`.
    ///
    /// * Overlaps are resolved by the rules engine: Abhijit Muhurta 16:00â€“16:48 with Dur Muhurtam 16:06â€“16:27 gives
    ///   16:00â€“16:06 Favorable, 16:06â€“16:27 Caution (conflict `DUR_MUHURTAM`), 16:27â€“16:48 Favorable; Amrit Kalam
    ///   overlapping Rahu Kalam is Mixed with conflict `RAHU_KALAM`.
    /// * Only segments with `isGoodTime: true` may be shown as a Good Time: Highly Favorable or Favorable, no conflict,
    ///   and at least one muhurta (24 min) long. `goodTimes` lists them best first.
    /// * When none qualifies, `goodTimes` is empty with `noGoodTimeReason` and a message. Mixed, Caution or Avoid
    ///   segments are never promoted.
    /// * For today, `nowSegmentId` and `nextGoodTimeId` give "Good time now" and "Next good time".
    ///
    /// Clients must branch on `status`, `isGoodTime` and the codes, never on labels.
    /// </remarks>
    /// <param name="query">Location, tradition, language, date, activity and optional description.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("good-times", Name = "GetGoodTimes")]
    [ProducesResponseType<GoodTimesResponse>(StatusCodes.Status200OK, Json, Description = "The evaluated day.")]
    public async Task<GoodTimesResponse> GetGoodTimesAsync([FromQuery] GoodTimesQuery query, CancellationToken cancellationToken)
    {
        var result = await muhurta.GetGoodTimesAsync(
            query.ToQuery(LanguagePreferences.FromRequest(Request, query.Language)),
            new GoodTimesRequest(query.Date, query.Activity, query.OtherText),
            cancellationToken);

        LanguagePreferences.SetResponseLanguage(Response, result.Language);
        return result.ToResponse();
    }

    /// <summary>"Why this time?" explanation for good times.</summary>
    /// <remarks>
    /// A short narrative for the same evaluation as `good-times` (same parameters). When generative AI is enabled
    /// (`Ai:Provider`: Anthropic, OpenAI or AzureOpenAI) the text is written by the model from the structured evaluation
    /// and accepted only if it mentions no time or segment outside it and calls nothing but a Good Time good; otherwise
    /// the deterministic template is returned (`source: template`, with `fallbackReason`). The explanation never
    /// changes statuses or times. It is requested separately so model latency never delays the recommendation;
    /// results are cached, and the endpoint has a stricter rate limit.
    /// </remarks>
    /// <param name="query">The same parameters as `good-times`.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("good-times/explanation", Name = "GetGoodTimesExplanation")]
    [EnableRateLimiting(RateLimitPolicyNames.AstrologyExpensive)]
    [ProducesResponseType<GoodTimeExplanationResponse>(StatusCodes.Status200OK, Json, Description = "The explanation.")]
    public async Task<ActionResult<GoodTimeExplanationResponse>> GetExplanationAsync([FromQuery] GoodTimesQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(User.FindFirstValue(StandardClaimNames.UserId)))
        {
            return Unauthorized();
        }

        // The package includes the validated issuer in the key to isolate users across providers and tenants.
        await using var lease = await concurrency.TryAcquireAsync(identityResolver.Resolve(HttpContext), cancellationToken);
        if (!lease.IsAcquired)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Calculation capacity is temporarily unavailable. Please retry.");
        }

        using var timeout = timeouts.CreateToken(TimeoutType.ExpensiveCalculation, cancellationToken);
        try
        {
            var result = await muhurta.GetExplanationAsync(
                query.ToQuery(LanguagePreferences.FromRequest(Request, query.Language)),
                new GoodTimesRequest(query.Date, query.Activity, query.OtherText),
                timeout.Token);

            LanguagePreferences.SetResponseLanguage(Response, result.Language);
            return result.ToResponse();
        }
        catch (OperationCanceledException) when (timeout.IsTimedOut)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Calculation timed out. Please retry.");
        }
    }

    /// <summary>Day statuses for calendar tiles.</summary>
    /// <remarks>
    /// One resolved status per day (`general` activity by default), the number of Good Times and the best one. The
    /// same values appear as `goodTime` on every Panchang day; clients must not derive day status from raw Panchang
    /// values.
    /// </remarks>
    /// <param name="query">Location, tradition, language, start date, number of days and optional activity.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("day-summaries", Name = "GetMuhurtaDaySummaries")]
    [ProducesResponseType<DaySummariesResponse>(StatusCodes.Status200OK, Json, Description = "One summary per day, in date order.")]
    public async Task<DaySummariesResponse> GetDaySummariesAsync([FromQuery] DaySummariesQuery query, CancellationToken cancellationToken)
    {
        var result = await muhurta.GetDaySummariesAsync(
            query.ToQuery(LanguagePreferences.FromRequest(Request, query.Language)),
            query.Start.GetValueOrDefault(),
            query.Days,
            query.Activity,
            query.OtherText,
            cancellationToken);

        LanguagePreferences.SetResponseLanguage(Response, result.Language);
        return result.ToResponse();
    }

    /// <summary>Next best Good Times for an activity.</summary>
    /// <remarks>
    /// Searches from now at the location (default 30 days) and returns the best options, at most one per day: that
    /// day's highest-ranked Good Time that has not ended. Highly Favorable ranks before Favorable, then the soonest.
    /// Each option's `segment` is the same segment (same `id`) as `good-times` returns for its `date`, so clients can
    /// open that day for details. Only Good Times are returned; when none qualifies, `options` is empty with
    /// `noGoodTimeMessage`.
    /// </remarks>
    /// <param name="query">Location, tradition, language, activity, optional description, count and days.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("good-times/best", Name = "GetBestGoodTimes")]
    [ProducesResponseType<BestGoodTimesResponse>(StatusCodes.Status200OK, Json, Description = "The best options, best first.")]
    public async Task<BestGoodTimesResponse> GetBestGoodTimesAsync([FromQuery] BestGoodTimesQuery query, CancellationToken cancellationToken)
    {
        var result = await muhurta.GetBestGoodTimesAsync(
            query.ToQuery(LanguagePreferences.FromRequest(Request, query.Language)),
            new BestGoodTimesRequest(query.Activity, query.OtherText, query.Count, query.Days),
            cancellationToken);

        LanguagePreferences.SetResponseLanguage(Response, result.Language);
        return result.ToResponse();
    }
}
