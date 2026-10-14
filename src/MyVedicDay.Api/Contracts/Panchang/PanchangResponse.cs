using System.Text.Json.Serialization;
using MyVedicDay.Api.Contracts.Festivals;
using MyVedicDay.Api.Contracts.Locations;
using MyVedicDay.Api.Contracts.Muhurta;

namespace MyVedicDay.Api.Contracts.Panchang;

/// <summary>Panchang for one or more consecutive local dates at one location.</summary>
/// <param name="Location">Location actually used after normalization and time zone resolution.</param>
/// <param name="Tradition">Panchang tradition (regional convention) that selected, ordered and named the timings.</param>
/// <param name="CalculationMethod">Calculation method used, e.g. <c>drik</c>. Traditions sharing a method share calculations.</param>
/// <param name="CalculationVersion">Version of the calculation algorithms and rule tables.</param>
/// <param name="LocalToday">Current local date at the location; clients use it to detect a day change.</param>
/// <param name="LocalTodayDisplay"><paramref name="LocalToday"/> in the location's date format, e.g. <c>09/28/2026</c>.</param>
/// <param name="DisplayFormat">
/// Format of every <c>…Display</c> value: the location's date convention, 24-hour time and the time zone abbreviation in
/// force at that instant, e.g. <c>09/28/2026 07:14:52 CDT</c> (US) or <c>28/09/2026 06:05:58 IST</c> (India).
/// Display values are for showing to users; use the ISO 8601 values for sorting and arithmetic.
/// </param>
/// <param name="GeneratedAt">UTC time the response was generated.</param>
/// <param name="Language">Language of all names and glossary text (also sent as <c>Content-Language</c>).</param>
/// <param name="Glossary">
/// Meaning, description, message and icon for every item kind, included once per response rather than per item.
/// Sections: <c>periods</c> (keyed by period <c>kind</c>), <c>elements</c> (<c>tithi</c>, <c>nakshatra</c>, <c>yoga</c>,
/// <c>karana</c>), <c>terms</c> (<c>sunrise</c>, <c>sunset</c>, <c>vara</c>) and <c>classifications</c> (keyed by
/// period <c>classification</c>).
/// </param>
/// <param name="Days">One entry per date, in order.</param>
public sealed record PanchangResponse(
    LocationResponse Location,
    TraditionResponse Tradition,
    string CalculationMethod,
    string CalculationVersion,
    DateOnly LocalToday,
    string LocalTodayDisplay,
    DisplayFormatResponse DisplayFormat,
    DateTimeOffset GeneratedAt,
    string Language,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, GlossaryEntryResponse>> Glossary,
    IReadOnlyList<DailyPanchangResponse> Days);

/// <summary>
/// Panchang for one local date. All times include the location's UTC offset at that moment; a period's start or end
/// may fall on the previous or next calendar date.
/// </summary>
/// <param name="Date">Local calendar date (ISO 8601).</param>
/// <param name="DateDisplay">Local date in the location's format, e.g. <c>09/28/2026</c> or <c>28/09/2026</c>.</param>
/// <param name="Weekday">Day of the week (Vara) as a stable English key, e.g. <c>Tuesday</c>.</param>
/// <param name="WeekdayName">Localized weekday name.</param>
/// <param name="Sunrise">Sunrise on this date.</param>
/// <param name="Sunset">Sunset on this date.</param>
/// <param name="NextSunrise">Next day's sunrise, the end of this Panchang day.</param>
/// <param name="SunriseDisplay"><paramref name="Sunrise"/> in the location's display format.</param>
/// <param name="SunsetDisplay"><paramref name="Sunset"/> in the location's display format.</param>
/// <param name="NextSunriseDisplay"><paramref name="NextSunrise"/> in the location's display format.</param>
/// <param name="Tithis">Tithi at sunrise followed by any Tithi that begins before the next sunrise. Glossary: <c>elements.tithi</c>.</param>
/// <param name="Nakshatras">Nakshatra at sunrise followed by any transitions before the next sunrise. Glossary: <c>elements.nakshatra</c>.</param>
/// <param name="Yogas">Yoga at sunrise followed by any transitions before the next sunrise. Glossary: <c>elements.yoga</c>.</param>
/// <param name="Karanas">Karana at sunrise followed by any transitions before the next sunrise. Glossary: <c>elements.karana</c>.</param>
/// <param name="AuspiciousPeriods">Favorable Muhurta-style periods shown by the tradition (Brahma Muhurta, Abhijit Muhurta, Amrit Kalam).</param>
/// <param name="InauspiciousPeriods">Unfavorable periods shown by the tradition (Rahu Kalam, Yamagandam, Gulika Kalam, Dur Muhurtam, Varjyam).</param>
/// <param name="Guidance">
/// Simple daily guidance for the selected tradition: a rating from the five Panchanga limbs (this application's
/// methodology, not a universal rule and not a Muhurta), a localized headline and summary, and the day's recommended and
/// traditionally avoided time windows. Windows never change the day rating. Where a favorable period overlaps a caution
/// or avoid period (e.g. Amrit Kalam with Rahu Kalam), the overlap is listed in <c>mixedWindows</c>.
/// </param>
/// <param name="Festivals">
/// Festivals and observances of the selected tradition on this date (empty on most days), with regional names. Same
/// items as <c>GET /api/v1/festivals/upcoming</c>.
/// </param>
/// <param name="GoodTime">
/// The day's resolved Find a Good Time status for the <c>general</c> activity: day status, number of Good Times and the
/// best one. Use it for calendar tiles and the Home "Good Time" card; never derive a Good Time from the periods above.
/// Same values as <c>GET /api/v1/muhurta/day-summaries</c>.
/// </param>
/// <param name="Choghadiya">
/// The 8 day and 8 night Choghadiya segments, when the tradition shows Choghadiya (e.g. <c>north-indian</c>,
/// <c>gujarati</c>, <c>all</c>); omitted otherwise.
/// </param>
public sealed record DailyPanchangResponse(
    DateOnly Date,
    string DateDisplay,
    string Weekday,
    string WeekdayName,
    DateTimeOffset Sunrise,
    DateTimeOffset Sunset,
    DateTimeOffset NextSunrise,
    string SunriseDisplay,
    string SunsetDisplay,
    string NextSunriseDisplay,
    IReadOnlyList<PanchangElementResponse> Tithis,
    IReadOnlyList<PanchangElementResponse> Nakshatras,
    IReadOnlyList<PanchangElementResponse> Yogas,
    IReadOnlyList<PanchangElementResponse> Karanas,
    IReadOnlyList<TimePeriodResponse> AuspiciousPeriods,
    IReadOnlyList<TimePeriodResponse> InauspiciousPeriods,
    DailyGuidanceResponse Guidance,
    IReadOnlyList<FestivalResponse> Festivals,
    MuhurtaDaySummaryResponse GoodTime,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TimePeriodResponse>? Choghadiya);

/// <summary>Daily guidance for one day.</summary>
/// <param name="Rating"><c>HighlyFavorable</c>, <c>Favorable</c>, <c>Mixed</c>, <c>Caution</c> or <c>Avoid</c>.</param>
/// <param name="RatingName">Localized rating label, e.g. "Favorable" or "Traditionally Avoided".</param>
/// <param name="Icon">Rating indicator: 🟢🟢, 🟢, 🟡, 🟠 or 🔴.</param>
/// <param name="Headline">Localized headline, e.g. "🟢 Favorable Day".</param>
/// <param name="Summary">Localized summary: the day's character, supportive and cautionary factors, best time, favorable and avoided windows.</param>
/// <param name="Factors">Tithi, Vara, Nakshatra, Yoga and Karana (the value in force for most of the daytime), each with its rating and traditional reason.</param>
/// <param name="BestWindows">Longest daytime stretches free of the tradition's caution and avoid periods.</param>
/// <param name="FavorableWindows">Favorable periods the tradition shows, e.g. Abhijit Muhurta, Amrit Kalam.</param>
/// <param name="SpiritualWindows">Periods suited to spiritual practice, e.g. Brahma Muhurta.</param>
/// <param name="CautionWindows">Periods to use with caution, e.g. Gulika Kalam.</param>
/// <param name="AvoidWindows">Periods traditionally avoided for new beginnings, e.g. Rahu Kalam, Varjyam, Durmuhurtham.</param>
/// <param name="MixedWindows">
/// Stretches where a favorable period overlaps a caution or avoid period, e.g. Amrit Kalam 14:58–16:27 with Rahu Kalam
/// 14:49–16:18 gives a mixed window 14:58–16:18. The periods come from different Panchanga rules and neither cancels the
/// other; each keeps its full times in the lists above.
/// </param>
/// <param name="Methodology">What this guidance is and is not.</param>
/// <param name="RulesVersion">Version of the guidance rules.</param>
public sealed record DailyGuidanceResponse(
    string Rating,
    string RatingName,
    string Icon,
    string Headline,
    string Summary,
    IReadOnlyList<GuidanceFactorResponse> Factors,
    IReadOnlyList<GuidanceWindowResponse> BestWindows,
    IReadOnlyList<TimePeriodResponse> FavorableWindows,
    IReadOnlyList<TimePeriodResponse> SpiritualWindows,
    IReadOnlyList<TimePeriodResponse> CautionWindows,
    IReadOnlyList<TimePeriodResponse> AvoidWindows,
    IReadOnlyList<MixedWindowResponse> MixedWindows,
    string Methodology,
    string RulesVersion);

/// <summary>One Panchanga limb's contribution to the day's guidance.</summary>
/// <param name="Limb"><c>tithi</c>, <c>vara</c>, <c>nakshatra</c>, <c>yoga</c> or <c>karana</c>.</param>
/// <param name="LimbName">Localized limb name.</param>
/// <param name="Key">Value rated, e.g. <c>Rohini</c>.</param>
/// <param name="Name">Localized value name.</param>
/// <param name="Rating">Rating of the value.</param>
/// <param name="RatingName">Localized rating label.</param>
/// <param name="Reason">Localized explanation, e.g. "Nakshatra Rohini: Highly Favorable. A fixed (Dhruva) Nakshatra…".</param>
public sealed record GuidanceFactorResponse(string Limb, string LimbName, string Key, string Name, string Rating, string RatingName, string Reason);

/// <summary>A recommended time window.</summary>
/// <param name="Start">Window start.</param>
/// <param name="End">Window end.</param>
/// <param name="StartDisplay"><paramref name="Start"/> in the location's display format.</param>
/// <param name="EndDisplay"><paramref name="End"/> in the location's display format.</param>
public sealed record GuidanceWindowResponse(DateTimeOffset Start, DateTimeOffset End, string StartDisplay, string EndDisplay);

/// <summary>A stretch where favorable and unfavorable periods overlap.</summary>
/// <param name="Start">Start of the overlap.</param>
/// <param name="End">End of the overlap.</param>
/// <param name="StartDisplay"><paramref name="Start"/> in the location's display format.</param>
/// <param name="EndDisplay"><paramref name="End"/> in the location's display format.</param>
/// <param name="Rating">Always <c>Mixed</c>.</param>
/// <param name="RatingName">Localized rating label, e.g. "Mixed".</param>
/// <param name="Icon">Rating indicator: 🟡.</param>
/// <param name="FavorablePeriods">Favorable periods in force during the overlap, with their full times, e.g. Amrit Kalam.</param>
/// <param name="UnfavorablePeriods">Caution or avoid periods in force during the overlap, with their full times, e.g. Rahu Kalam.</param>
public sealed record MixedWindowResponse(
    DateTimeOffset Start,
    DateTimeOffset End,
    string StartDisplay,
    string EndDisplay,
    string Rating,
    string RatingName,
    string Icon,
    IReadOnlyList<TimePeriodResponse> FavorablePeriods,
    IReadOnlyList<TimePeriodResponse> UnfavorablePeriods);

/// <summary>A Tithi, Nakshatra, Yoga or Karana with its exact start and end.</summary>
/// <param name="Key">Stable, language-independent identifier, e.g. <c>ShuklaPanchami</c> or <c>Rohini</c>.</param>
/// <param name="Number">Ordinal of the element (Tithi 1–30, Nakshatra 1–27, Yoga 1–27, Karana 1–11).</param>
/// <param name="Name">Localized name, e.g. "Shukla Panchami" or "Rohini".</param>
/// <param name="Start">When the element begins.</param>
/// <param name="End">When the element ends and the next one begins.</param>
/// <param name="StartDisplay"><paramref name="Start"/> in the location's display format.</param>
/// <param name="EndDisplay"><paramref name="End"/> in the location's display format.</param>
/// <param name="ShortName">Optional backend-defined abbreviated label, e.g. Chatur for Tithi.</param>
/// <param name="CompactName">Optional compact code, e.g. P4 or A15.</param>
public sealed record PanchangElementResponse(
    string Key, int Number, string Name, DateTimeOffset Start, DateTimeOffset End, string StartDisplay, string EndDisplay,
    string? ShortName = null, string? CompactName = null);

/// <summary>A classified time period. Its meaning, description and message are in <c>glossary.periods[kind]</c>.</summary>
/// <param name="Kind">Stable identifier and glossary key, e.g. <c>RahuKalam</c>.</param>
/// <param name="Name">Localized display name, e.g. "Rahu Kalam".</param>
/// <param name="Classification">
/// <c>HighlyFavorable</c>, <c>Favorable</c>, <c>Neutral</c>, <c>Caution</c> or <c>Avoid</c>; label and icon are in
/// <c>glossary.classifications[classification]</c>.
/// </param>
/// <param name="Start">Period start.</param>
/// <param name="End">Period end.</param>
/// <param name="StartDisplay"><paramref name="Start"/> in the location's display format.</param>
/// <param name="EndDisplay"><paramref name="End"/> in the location's display format.</param>
/// <param name="Overlaps">
/// Favorable periods only: the caution or avoid periods that overlap this one, with the overlapping times to avoid,
/// e.g. Rahu Kalam 14:58–16:18 within Amrit Kalam 14:58–16:27. Omitted when there are none.
/// </param>
/// <param name="OverlapMessage">
/// Favorable periods only: localized note naming the times to avoid, e.g. "Overlaps Rahu Kalam (14:49–16:18 IST): avoid
/// 14:58–16:18 IST for new beginnings." Omitted when there are no overlaps.
/// </param>
public sealed record TimePeriodResponse(
    string Kind,
    string Name,
    string Classification,
    DateTimeOffset Start,
    DateTimeOffset End,
    string StartDisplay,
    string EndDisplay,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PeriodOverlapResponse>? Overlaps = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OverlapMessage = null);

/// <summary>A caution or avoid period overlapping a favorable period.</summary>
/// <param name="Kind">Kind of the overlapping period, e.g. <c>RahuKalam</c>.</param>
/// <param name="Name">Localized name of the overlapping period.</param>
/// <param name="Classification"><c>Caution</c> or <c>Avoid</c>.</param>
/// <param name="Start">Start of the overlap: the time to avoid begins.</param>
/// <param name="End">End of the overlap: the time to avoid ends.</param>
/// <param name="StartDisplay"><paramref name="Start"/> in the location's display format.</param>
/// <param name="EndDisplay"><paramref name="End"/> in the location's display format.</param>
public sealed record PeriodOverlapResponse(
    string Kind, string Name, string Classification, DateTimeOffset Start, DateTimeOffset End, string StartDisplay, string EndDisplay);

/// <summary>Format of the display strings in a response.</summary>
/// <param name="DatePattern">Date pattern, e.g. <c>MM/dd/yyyy</c> (US), <c>dd/MM/yyyy</c> (India), <c>dd.MM.yyyy</c> (Germany).</param>
/// <param name="TimePattern">Time pattern, e.g. <c>HH:mm:ss</c>.</param>
/// <param name="CountryCode">Country whose convention was applied; null when unknown (the default pattern is used).</param>
/// <param name="Example">A value in this format (the first day's sunrise), e.g. <c>09/28/2026 07:14:52 CDT</c>.</param>
public sealed record DisplayFormatResponse(string DatePattern, string TimePattern, string? CountryCode, string Example);

/// <summary>Localized explanation of a Panchang item.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Meaning">What the item means (full explanation).</param>
/// <param name="Description">Short, app-friendly description.</param>
/// <param name="Message">One-line message for compact UI, e.g. "Avoid important new beginnings".</param>
/// <param name="Icon">Visual indicator, e.g. 🔴 for <c>Avoid</c>.</param>
public sealed record GlossaryEntryResponse(string Name, string? Meaning, string? Description, string? Message, string? Icon);

/// <summary>A tradition as Panchang presents it: its name and the timings it shows.</summary>
/// <param name="Key">Identifier to pass as <c>tradition</c>, e.g. <c>telugu</c>.</param>
/// <param name="Name">Localized name, e.g. "Telugu".</param>
/// <param name="Description">Localized description of what the tradition shows.</param>
/// <param name="Display">
/// Timings shown, in order of emphasis: period kinds (e.g. <c>Varjyam</c>, <c>DurMuhurtam</c>) or <c>Choghadiya</c>.
/// Clients can use this order to arrange the day view.
/// </param>
public sealed record TraditionResponse(string Key, string Name, string? Description, IReadOnlyList<string> Display);

/// <summary>Panchang's profile of every tradition.</summary>
/// <param name="Language">Language of names and descriptions.</param>
/// <param name="Traditions">Traditions in display order; <c>all</c> shows every timing.</param>
public sealed record PanchangTraditionsResponse(string Language, IReadOnlyList<TraditionResponse> Traditions);

/// <summary>The glossary in one language.</summary>
/// <param name="Language">Language of the text.</param>
/// <param name="Sections">Entries by section (<c>periods</c>, <c>elements</c>, <c>terms</c>, <c>classifications</c>), then by key.</param>
public sealed record GlossaryResponse(
    string Language, IReadOnlyDictionary<string, IReadOnlyDictionary<string, GlossaryEntryResponse>> Sections);
