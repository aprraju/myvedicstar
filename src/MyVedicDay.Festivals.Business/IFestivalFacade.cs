using MyVedicDay.Localization;
using MyVedicDay.Location;

namespace MyVedicDay.Festivals.Business;

/// <summary>Where and for which tradition to list festivals.</summary>
/// <param name="Location">The location: festival dates depend on its sunrise and sunset.</param>
/// <param name="Tradition">Optional tradition key; the configured default (<c>all</c>) when omitted.</param>
/// <param name="PreferredLanguages">Language tags in order of preference.</param>
public sealed record FestivalQuery(LocationQuery Location, string? Tradition = null, IReadOnlyList<string>? PreferredLanguages = null);

/// <summary>Festivals at one location between two local dates, for one tradition.</summary>
/// <param name="Location">The location actually used.</param>
/// <param name="Tradition">The tradition whose festivals and regional names were applied.</param>
/// <param name="LocalToday">Current local date at the location.</param>
/// <param name="LocalTodayDisplay"><paramref name="LocalToday"/> in the location's date format.</param>
/// <param name="From">First local date covered.</param>
/// <param name="To">Last local date covered.</param>
/// <param name="DisplayFormat">Format of every display string.</param>
/// <param name="GeneratedAt">UTC instant the result was assembled.</param>
/// <param name="Language">Language of names and descriptions.</param>
/// <param name="RulesVersion">Version of the festival rules.</param>
/// <param name="CalculationVersion">Version of the festival-date algorithms.</param>
/// <param name="Festivals">Festivals in date order.</param>
public sealed record FestivalsResult(
    LocationDetails Location,
    FestivalTradition Tradition,
    DateOnly LocalToday,
    string LocalTodayDisplay,
    DateOnly From,
    DateOnly To,
    DateTimeDisplayFormat DisplayFormat,
    DateTimeOffset GeneratedAt,
    string Language,
    string RulesVersion,
    string CalculationVersion,
    IReadOnlyList<Festival> Festivals);

/// <summary>The tradition applied to a festival list.</summary>
public sealed record FestivalTradition(string Key, string Name);

/// <summary>A festival or observance on a local date.</summary>
/// <param name="Key">Stable key, e.g. <c>GaneshChaturthi</c>.</param>
/// <param name="Name">Localized name, using the tradition's regional name (e.g. Vinayaka Chavithi for Telugu).</param>
/// <param name="Description">Localized description.</param>
/// <param name="Category"><c>festival</c> or <c>observance</c>.</param>
/// <param name="CategoryName">Localized category name.</param>
/// <param name="Icon">Category icon.</param>
/// <param name="Date">Local date of observance.</param>
/// <param name="DateDisplay"><paramref name="Date"/> in the location's date format.</param>
/// <param name="DaysFromToday">0 for today, 1 for tomorrow, negative for past dates.</param>
/// <param name="Timing">What determined the date.</param>
public sealed record Festival(
    string Key,
    string Name,
    string? Description,
    string Category,
    string CategoryName,
    string? Icon,
    DateOnly Date,
    string DateDisplay,
    int DaysFromToday,
    FestivalTiming Timing);

/// <summary>What determined a festival's date.</summary>
/// <param name="Basis"><c>Tithi</c>, <c>Nakshatra</c> or <c>Sankranti</c>.</param>
/// <param name="BasisKey">The Tithi, Nakshatra or sign, e.g. <c>ShuklaChaturthi</c>.</param>
/// <param name="Start">When the Tithi or Nakshatra begins, or the Sankranti instant.</param>
/// <param name="End">When the Tithi or Nakshatra ends, or the Sankranti instant.</param>
/// <param name="StartDisplay"><paramref name="Start"/> in the location's display format.</param>
/// <param name="EndDisplay"><paramref name="End"/> in the location's display format.</param>
/// <param name="ObservanceTime">Observance time (Kala) used, e.g. <c>Madhyahna</c>.</param>
/// <param name="ObservanceTimeName">Localized name of the observance time.</param>
/// <param name="ObservanceStart">The observance window on the festival day; null for days offset from it (e.g. Holi).</param>
/// <param name="ObservanceEnd">End of the observance window.</param>
/// <param name="ObservanceStartDisplay"><paramref name="ObservanceStart"/> in the location's display format.</param>
/// <param name="ObservanceEndDisplay"><paramref name="ObservanceEnd"/> in the location's display format.</param>
public sealed record FestivalTiming(
    string Basis,
    string BasisKey,
    DateTimeOffset Start,
    DateTimeOffset End,
    string StartDisplay,
    string EndDisplay,
    string ObservanceTime,
    string ObservanceTimeName,
    DateTimeOffset? ObservanceStart,
    DateTimeOffset? ObservanceEnd,
    string? ObservanceStartDisplay,
    string? ObservanceEndDisplay);

/// <summary>
/// Public entry point to festivals. "Today" is always the current local date at the requested location.
/// </summary>
/// <remarks>
/// Throws <see cref="MyVedicDay.Shared.Exceptions.ValidationException"/> for invalid locations, traditions, dates or
/// day counts, and <see cref="MyVedicDay.Shared.Exceptions.BusinessRuleException"/> when the sun does not rise or set.
/// </remarks>
public interface IFestivalFacade
{
    /// <summary>Festivals from the location's local today through the next <paramref name="days"/> − 1 days.</summary>
    Task<FestivalsResult> GetUpcomingAsync(FestivalQuery query, int days, CancellationToken cancellationToken);

    /// <summary>Festivals on <paramref name="days"/> consecutive local dates starting at <paramref name="startDate"/>.</summary>
    Task<FestivalsResult> GetRangeAsync(FestivalQuery query, DateOnly startDate, int days, CancellationToken cancellationToken);
}
