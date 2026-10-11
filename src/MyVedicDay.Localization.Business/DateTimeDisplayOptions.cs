using System.ComponentModel.DataAnnotations;

namespace MyVedicDay.Localization;

/// <summary>
/// How local dates and times are formatted for display. Bound from the <c>DateTimeDisplay</c> configuration section.
/// </summary>
/// <remarks>
/// The date pattern is chosen by the location's country: an explicit entry in <see cref="CountryDatePatterns"/>, else
/// (when <see cref="DeriveFromCountryConventions"/> is on) the country's standard short-date convention (day/month/year
/// order and separator, normalized to two-digit day and month and four-digit year), else <see cref="DefaultDatePattern"/>.
/// Patterns use NodaTime syntax: <c>dd</c>, <c>MM</c>, <c>yyyy</c>, <c>HH</c>, <c>mm</c>, <c>ss</c>.
/// </remarks>
public sealed class DateTimeDisplayOptions
{
    public const string SectionName = "DateTimeDisplay";

    /// <summary>Date pattern when the country is unknown or has no convention.</summary>
    [Required]
    public string DefaultDatePattern { get; set; } = "dd/MM/yyyy";

    /// <summary>Time pattern, e.g. <c>HH:mm:ss</c> (24-hour) or <c>hh:mm:ss tt</c> (12-hour).</summary>
    [Required]
    public string TimePattern { get; set; } = "HH:mm:ss";

    /// <summary>Compact time pattern for time ranges in sentences, e.g. <c>HH:mm</c> or <c>h:mm tt</c>.</summary>
    [Required]
    public string ShortTimePattern { get; set; } = "HH:mm";

    /// <summary>Use each country's standard date convention when it has no explicit pattern.</summary>
    public bool DeriveFromCountryConventions { get; set; } = true;

    /// <summary>Explicit date patterns by ISO 3166-1 alpha-2 country code; configuration entries add to or replace these.</summary>
    public Dictionary<string, string> CountryDatePatterns { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = "MM/dd/yyyy",
        ["IN"] = "dd/MM/yyyy",
    };
}
