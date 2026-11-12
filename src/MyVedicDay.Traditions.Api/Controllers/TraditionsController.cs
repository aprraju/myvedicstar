using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyVedicDay.Api.Contracts.Traditions;
using MyVedicDay.Api.Infrastructure.Localization;
using MyVedicDay.Traditions;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.RateLimiting;

namespace MyVedicDay.Api.Controllers.Traditions;

/// <summary>
/// Regional traditions a user can choose. Shared by every feature; each feature applies its own rules per tradition.
/// </summary>
[ApiController]
[Authorize(AuthPolicyNames.AnyProvider)]
[EnableRateLimiting(RateLimitPolicyNames.PublicRead)]
[Route("api/v1/traditions")]
[Tags("Traditions")]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json",
    Description = "Invalid language tag.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json",
    Description = "Rate limit exceeded for this client. Retry after the current window.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json",
    Description = "Unexpected error. Quote the traceId or correlationId when reporting it.")]
public sealed class TraditionsController(ITraditionFacade traditions) : ControllerBase
{
    /// <summary>List traditions.</summary>
    /// <remarks>
    /// Returns every tradition (`all`, `telugu`, `tamil`, `kannada`, `malayalam`, `north-indian`, `gujarati`, `marathi`)
    /// with its localized name and region, for a tradition picker. The user's choice is passed as `tradition` to every
    /// feature. Location decides the calculation and tradition decides the conventions, so a Telugu family in Dallas
    /// uses `telugu` with Dallas coordinates.
    ///
    /// What a tradition means for a feature is feature-specific: see `GET /api/v1/panchang/traditions` for the
    /// Panchang timings each tradition shows.
    /// </remarks>
    /// <param name="language">Optional language tag, e.g. <c>en</c>, <c>te</c>.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet(Name = "GetTraditions")]
    [ProducesResponseType<TraditionsResponse>(StatusCodes.Status200OK, "application/json",
        Description = "Available traditions in the negotiated language.")]
    public async Task<TraditionsResponse> GetAsync(
        [FromQuery, RegularExpression(LanguagePreferences.LanguageTagPattern)] string? language,
        CancellationToken cancellationToken)
    {
        var languages = LanguagePreferences.FromRequest(Request, language);
        var result = await traditions.GetTraditionsAsync(languages, cancellationToken);
        LanguagePreferences.SetResponseLanguage(Response, result.Language);
        return result.ToResponse();
    }
}
