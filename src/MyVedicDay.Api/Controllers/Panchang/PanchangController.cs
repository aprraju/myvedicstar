using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyVedicDay.Api.Contracts.Panchang;
using MyVedicDay.Api.Infrastructure.Localization;
using MyVedicDay.Festivals.Business;
using MyVedicDay.Muhurta.Business;
using MyVedicDay.Panchang.Business;
using MyVedicDay.Panchang.Business.Models;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.RateLimiting;

namespace MyVedicDay.Api.Controllers.Panchang;

/// <summary>
/// Location-aware Vedic Panchang calendar. "Today" is the current local date at the requested location.
/// </summary>
[ApiController]
[Authorize(AuthPolicyNames.AnyProvider)]
[EnableRateLimiting(RateLimitPolicyNames.PublicRead)]
[Route("api/v1/panchang")]
[Tags("Panchang")]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json",
    Description = "Invalid parameters, unknown time zone or tradition, date out of range, or no matching city.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json",
    Description = "The Panchang is undefined for this location and date (no sunrise or sunset during polar day or night).")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json",
    Description = "Rate limit exceeded for this client. Retry after the current window.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json",
    Description = "Unexpected error. Quote the traceId or correlationId when reporting it.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json",
    Description = "A dependency is temporarily unavailable. Retry later.")]
public sealed class PanchangController(IPanchangFacade panchang, IFestivalFacade festivals, IMuhurtaFacade muhurta) : ControllerBase
{
    private const string Json = "application/json";

    private PanchangQuery ToQuery(PanchangLocationRequest location) =>
        location.ToQuery(LanguagePreferences.FromRequest(Request, location.Language));

    /// <summary>
    /// Adds the festivals and the Good Time summary of the returned dates. Panchang, Festivals and Muhurta are separate
    /// feature modules (ADR-0008), so the host composes their facades; none depends on another's business layer.
    /// </summary>
    private async Task<PanchangResponse> RespondAsync(
        PanchangLocationRequest location, PanchangResult result, CancellationToken cancellationToken)
    {
        var languages = LanguagePreferences.FromRequest(Request, location.Language);
        var dayFestivals = await festivals.GetRangeAsync(location.ToFestivalQuery(languages), result.Days[0].Date, result.Days.Count, cancellationToken);
        var goodTimes = await muhurta.GetDaySummariesAsync(
            location.ToMuhurtaQuery(languages), result.Days[0].Date, result.Days.Count, activity: null, otherText: null, cancellationToken);

        LanguagePreferences.SetResponseLanguage(Response, result.Glossary.Language);
        return result.ToResponse(dayFestivals, goodTimes);
    }

    /// <summary>Panchang for today at the location.</summary>
    /// <remarks>
    /// Returns one day: the current **local** date at the location, which may differ from the server's or the
    /// caller's date (at 20:00 UTC it is already tomorrow in India but still today in Texas).
    ///
    /// Clients that stay open should compare `localToday` with the location's current date and refetch after local
    /// midnight. Cached results expire at the location's next local midnight.
    ///
    /// Examples: `?place=Austin, Texas, USA`, `?place=Hyderabad, Telangana&amp;latitude=17.4&amp;longitude=78.5`,
    /// `?latitude=17.385&amp;longitude=78.4867`.
    /// </remarks>
    /// <param name="location">Location and tradition.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("today", Name = "GetPanchangToday")]
    [AllowAnonymous]
    [ProducesResponseType<PanchangResponse>(StatusCodes.Status200OK, Json, Description = "Panchang for the location's current local date.")]
    public async Task<PanchangResponse> GetTodayAsync([FromQuery] PanchangLocationRequest location, CancellationToken cancellationToken) =>
        await RespondAsync(location, await panchang.GetTodayAsync(ToQuery(location), cancellationToken), cancellationToken);

    /// <summary>Panchang for tomorrow at the location.</summary>
    /// <remarks>Returns one day: the local date after `localToday` at the location.</remarks>
    /// <param name="location">Location and tradition.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("tomorrow", Name = "GetPanchangTomorrow")]
    [ProducesResponseType<PanchangResponse>(StatusCodes.Status200OK, Json, Description = "Panchang for the location's next local date.")]
    public async Task<PanchangResponse> GetTomorrowAsync([FromQuery] PanchangLocationRequest location, CancellationToken cancellationToken) =>
        await RespondAsync(location, await panchang.GetTomorrowAsync(ToQuery(location), cancellationToken), cancellationToken);

    /// <summary>Panchang for the next ten days.</summary>
    /// <remarks>
    /// Returns ten consecutive local dates starting with today at the location. Days are cached individually, so
    /// days already requested (for example today) are reused and only missing days are calculated.
    /// </remarks>
    /// <param name="location">Location and tradition.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("ten-days", Name = "GetPanchangTenDays")]
    [ProducesResponseType<PanchangResponse>(StatusCodes.Status200OK, Json, Description = "Ten consecutive days starting with local today.")]
    public async Task<PanchangResponse> GetTenDaysAsync([FromQuery] PanchangLocationRequest location, CancellationToken cancellationToken) =>
        await RespondAsync(location, await panchang.GetTenDaysAsync(ToQuery(location), cancellationToken), cancellationToken);

    /// <summary>Panchang for a range of dates.</summary>
    /// <remarks>
    /// Returns `days` consecutive local dates starting at `start` (1–366 days). Useful for custom calendar views
    /// such as a week or a Hindu month spanning two Gregorian months.
    /// </remarks>
    /// <param name="location">Location and tradition.</param>
    /// <param name="start">First local date, `yyyy-MM-dd`.</param>
    /// <param name="days">Number of consecutive days, 1–366.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("range", Name = "GetPanchangRange")]
    [ProducesResponseType<PanchangResponse>(StatusCodes.Status200OK, Json, Description = "The requested consecutive days.")]
    public async Task<PanchangResponse> GetRangeAsync(
        [FromQuery] PanchangLocationRequest location,
        [FromQuery, Required] DateOnly? start,
        [FromQuery, Range(1, 366)] int days,
        CancellationToken cancellationToken) =>
        await RespondAsync(location, await panchang.GetDateRangeAsync(ToQuery(location), start.GetValueOrDefault(), days, cancellationToken), cancellationToken);

    /// <summary>Panchang for a calendar month.</summary>
    /// <remarks>Returns every day of the Gregorian month (28–31 days) in date order.</remarks>
    /// <param name="year">Gregorian year, 1900–2100.</param>
    /// <param name="month">Gregorian month, 1–12.</param>
    /// <param name="location">Location and tradition.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("month/{year:int}/{month:int}", Name = "GetPanchangMonth")]
    [ProducesResponseType<PanchangResponse>(StatusCodes.Status200OK, Json, Description = "Every day of the month.")]
    public async Task<PanchangResponse> GetMonthAsync(
        int year, int month, [FromQuery] PanchangLocationRequest location, CancellationToken cancellationToken) =>
        await RespondAsync(location, await panchang.GetMonthAsync(ToQuery(location), year, month, cancellationToken), cancellationToken);

    /// <summary>Panchang for a calendar year.</summary>
    /// <remarks>
    /// Returns every day of the Gregorian year (365 or 366 days). The first request for a location calculates the
    /// whole year (a few hundred milliseconds); later requests for any part of it are served from cache.
    /// </remarks>
    /// <param name="year">Gregorian year, 1900–2100.</param>
    /// <param name="location">Location and tradition.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("year/{year:int}", Name = "GetPanchangYear")]
    [ProducesResponseType<PanchangResponse>(StatusCodes.Status200OK, Json, Description = "Every day of the year.")]
    public async Task<PanchangResponse> GetYearAsync(
        int year, [FromQuery] PanchangLocationRequest location, CancellationToken cancellationToken) =>
        await RespondAsync(location, await panchang.GetYearAsync(ToQuery(location), year, cancellationToken), cancellationToken);

    /// <summary>Panchang for a specific date.</summary>
    /// <remarks>
    /// Returns one day for the given local date at the location. The Panchang day runs from that date's sunrise to
    /// the next sunrise, so the last Tithi or Nakshatra listed usually ends on the following date.
    /// </remarks>
    /// <param name="date">Local calendar date, `yyyy-MM-dd`, between 1900-01-01 and 2100-12-31.</param>
    /// <param name="location">Location and tradition.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("{date}", Name = "GetPanchangForDate")]
    [ProducesResponseType<PanchangResponse>(StatusCodes.Status200OK, Json, Description = "Panchang for the requested date.")]
    public async Task<PanchangResponse> GetDateAsync(
        DateOnly date, [FromQuery] PanchangLocationRequest location, CancellationToken cancellationToken) =>
        await RespondAsync(location, await panchang.GetDateAsync(ToQuery(location), date, cancellationToken), cancellationToken);
}
