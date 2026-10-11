using System.ComponentModel.DataAnnotations;
using MyVedicDay.Panchang.Business.Models;

namespace MyVedicDay.Panchang.Business.Configuration;

/// <summary>
/// Panchang business settings. Bound from the <c>Panchang</c> configuration section and validated at startup.
/// </summary>
public sealed class PanchangOptions
{
    public const string SectionName = "Panchang";

    /// <summary>Largest number of days a single range request may return.</summary>
    [Range(1, 400)]
    public int MaxRangeDays { get; set; } = 366;

    /// <summary>Cache lifetime for dates that have already ended at the location.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "30.00:00:00")]
    public TimeSpan PastDateCacheLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Upper bound on any cache entry's lifetime, so far-future dates do not occupy the cache indefinitely.</summary>
    [Range(typeof(TimeSpan), "01:00:00", "400.00:00:00")]
    public TimeSpan MaxCacheLifetime { get; set; } = TimeSpan.FromDays(35);

    /// <summary>
    /// Presentation classification per period kind. Configuration entries override these defaults individually;
    /// every period kind must have a classification.
    /// </summary>
    public Dictionary<string, PeriodClassification> PeriodClassifications { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RahuKalam"] = PeriodClassification.Avoid,
        ["Yamagandam"] = PeriodClassification.Avoid,
        ["DurMuhurtam"] = PeriodClassification.Avoid,
        ["GulikaKalam"] = PeriodClassification.Caution,
        ["Varjyam"] = PeriodClassification.Avoid,
        ["BrahmaMuhurta"] = PeriodClassification.Favorable,
        ["AbhijitMuhurta"] = PeriodClassification.Favorable,
        ["AmritKalam"] = PeriodClassification.HighlyFavorable,
        ["ChoghadiyaAmrit"] = PeriodClassification.HighlyFavorable,
        ["ChoghadiyaShubh"] = PeriodClassification.Favorable,
        ["ChoghadiyaLabh"] = PeriodClassification.Favorable,
        ["ChoghadiyaChal"] = PeriodClassification.Neutral,
        ["ChoghadiyaUdveg"] = PeriodClassification.Avoid,
        ["ChoghadiyaKaal"] = PeriodClassification.Avoid,
        ["ChoghadiyaRog"] = PeriodClassification.Avoid,
    };
}
