namespace MyVedicDay.Panchang.Business.Models;

/// <summary>
/// Panchang for one local calendar date, localized into the response language and filtered to the timings of the
/// selected Panchang tradition.
/// </summary>
/// <remarks>
/// All times carry the location's UTC offset at that instant, so DST changes and periods that cross midnight
/// are represented exactly: a period's start and end dates may differ from <see cref="Date"/>. Each time also has a
/// display string in the location's format (e.g. <c>09/28/2026 07:14:52 CDT</c> or <c>28/09/2026 06:05:58 IST</c>).
/// <see cref="Guidance"/> is the day's simple consumer guidance for the selected tradition.
/// </remarks>
public sealed record DailyPanchang(
    DateOnly Date,
    string DateDisplay,
    DayOfWeek Weekday,
    string WeekdayName,
    SunTimes Sun,
    SunTimesDisplay SunDisplay,
    IReadOnlyList<PanchangElementPeriod> Tithis,
    IReadOnlyList<PanchangElementPeriod> Nakshatras,
    IReadOnlyList<PanchangElementPeriod> Yogas,
    IReadOnlyList<PanchangElementPeriod> Karanas,
    IReadOnlyList<PanchangTimePeriod> Periods,
    DailyGuidance? Guidance = null);

/// <summary>Sunrise and sunset bounding the Panchang day.</summary>
public sealed record SunTimes(DateTimeOffset Sunrise, DateTimeOffset Sunset, DateTimeOffset NextSunrise);

/// <summary>Sunrise and sunset formatted for display in the location's format.</summary>
public sealed record SunTimesDisplay(string Sunrise, string Sunset, string NextSunrise);

/// <summary>
/// A Tithi, Nakshatra, Yoga or Karana. Lists are ordered: the first entry prevails at sunrise and each later
/// entry starts exactly when the previous one ends.
/// </summary>
/// <param name="Key">Stable, language-independent identifier, e.g. <c>ShuklaPanchami</c> or <c>Rohini</c>.</param>
/// <param name="Number">Ordinal (Tithi 1–30, Nakshatra 1–27, Yoga 1–27, Karana 1–11).</param>
/// <param name="Name">Localized display name.</param>
/// <param name="Start">When the element begins.</param>
/// <param name="End">When the element ends and the next begins.</param>
/// <param name="StartDisplay"><paramref name="Start"/> in the location's display format.</param>
/// <param name="EndDisplay"><paramref name="End"/> in the location's display format.</param>
public sealed record PanchangElementPeriod(
    string Key, int Number, string Name, DateTimeOffset Start, DateTimeOffset End, string StartDisplay, string EndDisplay);

/// <summary>A classified period such as Rahu Kalam, Varjyam, Abhijit Muhurta or a Choghadiya segment.</summary>
/// <param name="Kind">Stable, language-independent identifier, e.g. <c>RahuKalam</c>; also its glossary key.</param>
/// <param name="Group"><c>Muhurta</c> or <c>Choghadiya</c>.</param>
/// <param name="Name">Localized display name, using the tradition's regional name where it has one (e.g. Kuligai).</param>
/// <param name="Classification">Presentation classification (configurable per kind).</param>
/// <param name="IsAuspicious">Traditional nature of the period.</param>
/// <param name="Start">Period start.</param>
/// <param name="End">Period end.</param>
/// <param name="StartDisplay"><paramref name="Start"/> in the location's display format.</param>
/// <param name="EndDisplay"><paramref name="End"/> in the location's display format.</param>
/// <param name="Overlaps">
/// For a favorable period only: the caution or avoid periods that overlap it, e.g. Rahu Kalam within Amrit Kalam. Null
/// when there are none. The favorable period keeps its full times; the overlap is the part to avoid.
/// </param>
/// <param name="OverlapMessage">
/// Localized note naming the times to avoid within this period, e.g. "Overlaps Rahu Kalam (14:49–16:18 IST): avoid
/// 14:58–16:18 IST for new beginnings." Null when there are no overlaps.
/// </param>
public sealed record PanchangTimePeriod(
    string Kind,
    string Group,
    string Name,
    PeriodClassification Classification,
    bool IsAuspicious,
    DateTimeOffset Start,
    DateTimeOffset End,
    string StartDisplay,
    string EndDisplay,
    IReadOnlyList<PeriodOverlap>? Overlaps = null,
    string? OverlapMessage = null);

/// <summary>A caution or avoid period overlapping a favorable period.</summary>
/// <param name="Kind">Kind of the overlapping period, e.g. <c>RahuKalam</c>.</param>
/// <param name="Name">Localized name of the overlapping period.</param>
/// <param name="Classification"><see cref="PeriodClassification.Caution"/> or <see cref="PeriodClassification.Avoid"/>.</param>
/// <param name="Start">Start of the overlap (the later of the two starts).</param>
/// <param name="End">End of the overlap (the earlier of the two ends).</param>
/// <param name="StartDisplay"><paramref name="Start"/> in the location's display format.</param>
/// <param name="EndDisplay"><paramref name="End"/> in the location's display format.</param>
public sealed record PeriodOverlap(
    string Kind,
    string Name,
    PeriodClassification Classification,
    DateTimeOffset Start,
    DateTimeOffset End,
    string StartDisplay,
    string EndDisplay);
