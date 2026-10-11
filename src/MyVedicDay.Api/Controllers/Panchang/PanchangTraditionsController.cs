using MyVedicStar.Caching;
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
/// Panchang conventions per tradition: which timings each tradition shows, in order of emphasis.
/// </summary>
[ApiController]
[Authorize(AuthPolicyNames.AnyProvider)]
[EnableRateLimiting(RateLimitPolicyNames.PublicRead)]
[Route("api/v1/panchang/traditions")]
[Tags("Panchang")]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json",
    Description = "Invalid language tag.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json",
    Description = "Rate limit exceeded for this client. Retry after the current window.")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json",
    Description = "Unexpected error. Quote the traceId or correlationId when reporting it.")]
public sealed class PanchangTraditionsController(IPanchangTraditionFacade traditions, IApplicationCache? cache = null) : ControllerBase
{
    /// <summary>Panchang timings per tradition.</summary>
    /// <remarks>
    /// For every tradition, the Panchang timings it shows, in order of emphasis (`display`), with the tradition's name
    /// and a description of what it shows:
    ///
    /// | Tradition | Shows |
    /// |-----------|-------|
    /// | `all` (default) | Every timing, including Choghadiya |
    /// | `telugu` | Varjyam, Durmuhurtham, Rahu Kalam, Yamagandam, Gulika, Amrita Kalam, Abhijit Muhurtham, Brahma Muhurtham |
    /// | `tamil` | Rahu Kalam, Yamagandam, Kuligai, Amirtha Kalam, Abhijit Muhurtham, Brahma Muhurtham |
    /// | `kannada` | Rahu Kalam, Yamagandam, Gulika, Durmuhurtha, Amrita Kala, Abhijit Muhurtha, Brahma Muhurtha |
    /// | `malayalam` | Rahu Kalam, Yamagandam, Gulika, Amrutha Kalam, Abhijit Muhurtham, Brahma Muhurtham |
    /// | `north-indian` | Rahu Kaal, Choghadiya, Abhijit Muhurat, Amrit Kaal, Brahma Muhurat |
    /// | `gujarati` | Choghadiya, Rahu Kaal, Abhijit Muhurat, Amrit Kaal, Brahma Muhurat |
    /// | `marathi` | Rahu Kaal, Abhijit Muhurat, Choghadiya, Amrit Kaal, Brahma Muhurat |
    ///
    /// Profiles are configurable (`panchang-traditions.json`), so this response is authoritative over the table above.
    /// </remarks>
    /// <param name="language">Optional language tag, e.g. <c>en</c>, <c>te</c>.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet(Name = "GetPanchangTraditions")]
    [ProducesResponseType<PanchangTraditionsResponse>(StatusCodes.Status200OK, "application/json",
        Description = "Panchang profile of every tradition in the negotiated language.")]
    public async Task<PanchangTraditionsResponse> GetAsync(
        [FromQuery, RegularExpression(LanguagePreferences.LanguageTagPattern)] string? language,
        CancellationToken cancellationToken)
    {
        var languages = LanguagePreferences.FromRequest(Request, language);
        var result = await ReferenceCache.GetAsync(cache, "panchang-traditions", languages, null, token => traditions.GetTraditionsAsync(languages, token), cancellationToken);
        LanguagePreferences.SetResponseLanguage(Response, result.Language);
        return result.ToResponse();
    }
}
