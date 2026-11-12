using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyVedicDay.Api.Contracts.Panchang;
using MyVedicDay.Api.Infrastructure.Localization;
using MyVedicDay.Panchang.Business;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.RateLimiting;

namespace MyVedicDay.Api.Controllers.Panchang;

/// <summary>
/// Localized meanings, descriptions, messages and classification labels for every Panchang item.
/// </summary>
[ApiController]
[Authorize(AuthPolicyNames.AnyProvider)]
[EnableRateLimiting(RateLimitPolicyNames.PublicRead)]
[Route("api/v1/panchang/glossary")]
[Tags("Glossary")]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json",
    Description = "Invalid language tag or unknown tradition.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json",
    Description = "Rate limit exceeded for this client. Retry after the current window.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json",
    Description = "Unexpected error. Quote the traceId or correlationId when reporting it.")]
public sealed class GlossaryController(IGlossaryFacade glossary) : ControllerBase
{
    /// <summary>Glossary of Panchang items.</summary>
    /// <remarks>
    /// Returns the same `glossary` object that every Panchang response includes, for help screens or for clients
    /// that cache it once per language and tradition. Sections:
    ///
    /// * `periods`: the timings the tradition shows (all of them for `all`), each with `name`, `meaning`,
    ///   `description` and a one-line `message`. Names follow the tradition, e.g. Kuligai for `tamil`.
    /// * `elements`: Tithi, Nakshatra, Yoga, Karana.
    /// * `terms`: sunrise, sunset, vara, and choghadiya when the tradition shows it.
    /// * `classifications`: `HighlyFavorable`, `Favorable`, `Neutral`, `Caution`, `Avoid`, each with a label and `icon`.
    ///
    /// The language is chosen from `language`, then `Accept-Language`; untranslated fields fall back to the default
    /// language. The chosen language is returned in `language` and the `Content-Language` header.
    /// </remarks>
    /// <param name="language">Optional language tag, e.g. <c>en</c>, <c>te</c>, <c>hi-IN</c>.</param>
    /// <param name="tradition">Optional Panchang tradition, e.g. <c>telugu</c>; defaults to <c>all</c>.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet(Name = "GetGlossary")]
    [ProducesResponseType<GlossaryResponse>(StatusCodes.Status200OK, "application/json",
        Description = "Glossary in the negotiated language for the tradition.")]
    public async Task<GlossaryResponse> GetAsync(
        [FromQuery, RegularExpression(LanguagePreferences.LanguageTagPattern)] string? language,
        [FromQuery, RegularExpression("^[A-Za-z][A-Za-z0-9-]{0,31}$")] string? tradition,
        CancellationToken cancellationToken)
    {
        var languages = LanguagePreferences.FromRequest(Request, language);
        var result = await glossary.GetGlossaryAsync(languages, tradition, cancellationToken);
        LanguagePreferences.SetResponseLanguage(Response, result.Language);
        return result.ToResponse();
    }
}
