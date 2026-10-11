using System.ComponentModel.DataAnnotations;
using MyVedicDay.Api.Contracts.Locations;
using MyVedicDay.Api.Infrastructure.Localization;
using MyVedicDay.Muhurta.Business;

namespace MyVedicDay.Api.Contracts.Muhurta;

/// <summary>Location, tradition and language parameters shared by the Find a Good Time endpoints.</summary>
public class MuhurtaLocationRequest : LocationRequest
{
    /// <summary>
    /// Optional tradition: <c>all</c> (default), <c>telugu</c>, <c>tamil</c>, <c>kannada</c>, <c>malayalam</c>,
    /// <c>north-indian</c>, <c>gujarati</c> or <c>marathi</c>. Adjusts the Muhurta rules (e.g. Choghadiya for Gujarati
    /// business) and the regional names.
    /// </summary>
    [RegularExpression("^[A-Za-z][A-Za-z0-9-]{0,31}$")]
    public string? Tradition { get; init; }

    /// <summary>Optional response language (BCP 47). Takes precedence over <c>Accept-Language</c>.</summary>
    [RegularExpression(LanguagePreferences.LanguageTagPattern)]
    public string? Language { get; init; }

    /// <summary>Optional birth Nakshatra from the 27-star catalog (case-insensitive). Empty input uses the standard evaluation. Personal Tara Bala caps suitability; it never overrides existing restrictions. Abhijit is not supported.</summary>
    [StringLength(64)]
    public string? Nakshatra { get; set; }

    internal MuhurtaQuery ToQuery(IReadOnlyList<string> preferredLanguages) => new(ToLocationQuery(), Tradition, preferredLanguages, Nakshatra);
}

/// <summary>Parameters of <c>GET /api/v1/muhurta/good-times</c>.</summary>
public sealed class GoodTimesQuery : MuhurtaLocationRequest
{
    /// <summary>Local date, <c>yyyy-MM-dd</c>; today at the location when omitted.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// Activity key from <c>GET /api/v1/muhurta/activities</c>, e.g. <c>business</c>; <c>general</c> when omitted.
    /// </summary>
    [RegularExpression("^[A-Za-z][A-Za-z0-9-]{0,31}$")]
    public string? Activity { get; init; }

    /// <summary>Description of the activity, required when <c>activity=other</c> (up to 80 characters).</summary>
    [StringLength(200)]
    public string? OtherText { get; init; }
}

/// <summary>Parameters of <c>GET /api/v1/muhurta/day-summaries</c>.</summary>
public sealed class DaySummariesQuery : MuhurtaLocationRequest
{
    /// <summary>First local date, <c>yyyy-MM-dd</c>.</summary>
    [Required]
    public DateOnly? Start { get; init; }

    /// <summary>Number of consecutive days, 1–366.</summary>
    [Range(1, 366)]
    public int Days { get; init; } = 1;

    /// <summary>Activity key; <c>general</c> (the calendar and Home status) when omitted.</summary>
    [RegularExpression("^[A-Za-z][A-Za-z0-9-]{0,31}$")]
    public string? Activity { get; init; }

    /// <summary>Description of the activity, required when <c>activity=other</c> (up to 80 characters).</summary>
    [StringLength(200)]
    public string? OtherText { get; init; }
}

/// <summary>Parameters of <c>GET /api/v1/muhurta/good-times/best</c>.</summary>
public sealed class BestGoodTimesQuery : MuhurtaLocationRequest
{
    /// <summary>Activity key; <c>general</c> when omitted.</summary>
    [RegularExpression("^[A-Za-z][A-Za-z0-9-]{0,31}$")]
    public string? Activity { get; init; }

    /// <summary>Description of the activity, required when <c>activity=other</c> (up to 80 characters).</summary>
    [StringLength(200)]
    public string? OtherText { get; init; }

    /// <summary>Number of options, 1–10; 3 when omitted.</summary>
    [Range(1, 10)]
    public int? Count { get; init; }

    /// <summary>Days to search from today at the location, 1–90; 30 when omitted.</summary>
    [Range(1, 90)]
    public int? Days { get; init; }
}
