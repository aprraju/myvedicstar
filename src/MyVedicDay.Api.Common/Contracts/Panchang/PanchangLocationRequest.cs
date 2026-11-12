using System.ComponentModel.DataAnnotations;
using MyVedicDay.Api.Contracts.Locations;
using MyVedicDay.Api.Infrastructure.Localization;
using MyVedicDay.Festivals.Business;
using MyVedicDay.Muhurta.Business;
using MyVedicDay.Panchang.Business.Models;

namespace MyVedicDay.Api.Contracts.Panchang;

/// <summary>
/// Location, tradition and language query parameters shared by all Panchang endpoints.
/// </summary>
public sealed class PanchangLocationRequest : LocationRequest
{
    /// <summary>
    /// Optional Panchang tradition (regional convention): <c>all</c> (default), <c>telugu</c>, <c>tamil</c>, <c>kannada</c>,
    /// <c>malayalam</c>, <c>north-indian</c>, <c>gujarati</c> or <c>marathi</c>. Selects which timings and festivals are
    /// shown and their regional names; calculations always use the location.
    /// </summary>
    [RegularExpression("^[A-Za-z][A-Za-z0-9-]{0,31}$")]
    public string? Tradition { get; init; }

    /// <summary>
    /// Optional response language (BCP 47), e.g. <c>en</c>, <c>te</c>, <c>hi-IN</c>. Takes precedence over the
    /// <c>Accept-Language</c> header. Unsupported languages fall back to the default language.
    /// </summary>
    [RegularExpression(LanguagePreferences.LanguageTagPattern)]
    public string? Language { get; init; }

    internal PanchangQuery ToQuery(IReadOnlyList<string> preferredLanguages) => new(ToLocationQuery(), Tradition, preferredLanguages);

    internal FestivalQuery ToFestivalQuery(IReadOnlyList<string> preferredLanguages) => new(ToLocationQuery(), Tradition, preferredLanguages);

    internal MuhurtaQuery ToMuhurtaQuery(IReadOnlyList<string> preferredLanguages) => new(ToLocationQuery(), Tradition, preferredLanguages);
}
