using MyVedicDay.Api.Contracts.Locations;
using MyVedicDay.Api.Contracts.Panchang;

namespace MyVedicDay.Api.Contracts.Festivals;

/// <summary>Festivals at one location over a period, for one tradition.</summary>
/// <param name="Location">Location actually used after normalization and time zone resolution.</param>
/// <param name="Tradition">Tradition whose festivals and regional names were applied.</param>
/// <param name="LocalToday">Current local date at the location.</param>
/// <param name="LocalTodayDisplay"><paramref name="LocalToday"/> in the location's date format.</param>
/// <param name="From">First local date covered (today for the upcoming list).</param>
/// <param name="To">Last local date covered.</param>
/// <param name="DisplayFormat">Format of every <c>…Display</c> value.</param>
/// <param name="GeneratedAt">UTC time the response was generated.</param>
/// <param name="Language">Language of names and descriptions (also sent as <c>Content-Language</c>).</param>
/// <param name="RulesVersion">Version of the festival rules (this application's methodology, provisional).</param>
/// <param name="CalculationVersion">Version of the festival-date algorithms.</param>
/// <param name="Festivals">Festivals in date order; on one date, major festivals before recurring observances.</param>
public sealed record FestivalsResponse(
    LocationResponse Location,
    FestivalTraditionResponse Tradition,
    DateOnly LocalToday,
    string LocalTodayDisplay,
    DateOnly From,
    DateOnly To,
    DisplayFormatResponse DisplayFormat,
    DateTimeOffset GeneratedAt,
    string Language,
    string RulesVersion,
    string CalculationVersion,
    IReadOnlyList<FestivalResponse> Festivals);

/// <summary>The tradition applied.</summary>
/// <param name="Key">Tradition key, e.g. <c>telugu</c>.</param>
/// <param name="Name">Localized name, e.g. "Telugu".</param>
public sealed record FestivalTraditionResponse(string Key, string Name);

/// <summary>A festival or recurring observance on a local date.</summary>
/// <param name="Key">Stable, language-independent key, e.g. <c>GaneshChaturthi</c>.</param>
/// <param name="Name">Localized name in the tradition's usage, e.g. "Vinayaka Chavithi" (Telugu) or "Ganesh Chaturthi".</param>
/// <param name="Description">Localized description.</param>
/// <param name="Category"><c>festival</c> or <c>observance</c> (recurring vrata: Ekadashi, Pradosham, Purnima, Amavasya…).</param>
/// <param name="CategoryName">Localized category name.</param>
/// <param name="Icon">Category icon, e.g. 🪔.</param>
/// <param name="Date">Local date of observance.</param>
/// <param name="DateDisplay"><paramref name="Date"/> in the location's date format.</param>
/// <param name="DaysFromToday">0 today, 1 tomorrow; negative for past dates.</param>
/// <param name="Timing">What determined the date.</param>
public sealed record FestivalResponse(
    string Key,
    string Name,
    string? Description,
    string Category,
    string CategoryName,
    string? Icon,
    DateOnly Date,
    string DateDisplay,
    int DaysFromToday,
    FestivalTimingResponse Timing);

/// <summary>
/// What determined a festival's date: a Tithi or Nakshatra prevailing during an observance time (Kala) of the day, or a
/// Sankranti.
/// </summary>
/// <param name="Basis"><c>Tithi</c>, <c>Nakshatra</c> or <c>Sankranti</c>.</param>
/// <param name="BasisKey">Tithi or Nakshatra key (see <c>/panchang</c> elements), or the sign entered, e.g. <c>Makara</c>.</param>
/// <param name="Start">When the Tithi or Nakshatra begins, or the Sankranti instant.</param>
/// <param name="End">When the Tithi or Nakshatra ends, or the Sankranti instant.</param>
/// <param name="StartDisplay"><paramref name="Start"/> in the location's display format.</param>
/// <param name="EndDisplay"><paramref name="End"/> in the location's display format.</param>
/// <param name="ObservanceTime">
/// <c>Sunrise</c>, <c>Arunodaya</c>, <c>Purvahna</c>, <c>Madhyahna</c>, <c>Aparahna</c>, <c>Sunset</c>, <c>Pradosha</c> or
/// <c>Nishita</c>: the part of the day in which the Tithi or Nakshatra must prevail.
/// </param>
/// <param name="ObservanceTimeName">Localized name of the observance time.</param>
/// <param name="ObservanceStart">Observance window on the festival day, e.g. the Madhyahna puja time; omitted for days offset from it (e.g. Holi).</param>
/// <param name="ObservanceEnd">End of the observance window.</param>
/// <param name="ObservanceStartDisplay"><paramref name="ObservanceStart"/> in the location's display format.</param>
/// <param name="ObservanceEndDisplay"><paramref name="ObservanceEnd"/> in the location's display format.</param>
public sealed record FestivalTimingResponse(
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
