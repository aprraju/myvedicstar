using System.ComponentModel.DataAnnotations;
using MyVedicDay.Api.Contracts.Locations;
using MyVedicDay.Api.Infrastructure.Localization;
using MyVedicDay.Festivals.Business;

namespace MyVedicDay.Api.Contracts.Festivals;

/// <summary>
/// Location, tradition, language and period for the upcoming-festivals endpoint.
/// </summary>
public sealed class FestivalsRequest : LocationRequest
{
    /// <summary>
    /// Optional tradition: <c>all</c> (default), <c>telugu</c>, <c>tamil</c>, <c>kannada</c>, <c>malayalam</c>,
    /// <c>north-indian</c>, <c>gujarati</c> or <c>marathi</c>. Selects which festivals are listed and their regional
    /// names (e.g. Vinayaka Chavithi for Telugu); dates always come from the location.
    /// </summary>
    [RegularExpression("^[A-Za-z][A-Za-z0-9-]{0,31}$")]
    public string? Tradition { get; init; }

    /// <summary>
    /// Optional response language (BCP 47), e.g. <c>en</c>. Takes precedence over the <c>Accept-Language</c> header.
    /// </summary>
    [RegularExpression(LanguagePreferences.LanguageTagPattern)]
    public string? Language { get; init; }

    /// <summary>Number of days to look ahead, starting with today at the location: 1–90, default 30.</summary>
    [Range(1, 90)]
    public int Days { get; init; } = 30;

    internal FestivalQuery ToQuery(IReadOnlyList<string> preferredLanguages) => new(ToLocationQuery(), Tradition, preferredLanguages);
}
