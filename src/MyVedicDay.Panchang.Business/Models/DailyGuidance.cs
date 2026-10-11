namespace MyVedicDay.Panchang.Business.Models;

/// <summary>Guidance rating of a day or of one Panchanga limb.</summary>
public enum GuidanceRating
{
    HighlyFavorable = 1,
    Favorable,
    Mixed,
    Caution,
    Avoid,
}

/// <summary>
/// Simple consumer guidance for one day: the five Panchanga limbs rated with this application's methodology (not a
/// universal traditional rule, and not a Muhurta for a specific ceremony), plus the day's time windows. Time windows
/// never change the day rating: a favorable day with Rahu Kalam stays favorable, with Rahu Kalam listed as a window to
/// avoid. Periods come from independent rules and can overlap; an overlap of a favorable period with a caution or avoid
/// period is reported as a mixed window.
/// </summary>
/// <param name="Rating">Day rating from the five limbs.</param>
/// <param name="RatingName">Localized rating label, e.g. "Favorable".</param>
/// <param name="Icon">Rating indicator, e.g. 🟢.</param>
/// <param name="Headline">Localized headline, e.g. "🟢 Favorable Day".</param>
/// <param name="Summary">Localized summary: the day's character, supportive and cautionary factors, and the key windows.</param>
/// <param name="Factors">The five limbs (Tithi, Vara, Nakshatra, Yoga, Karana) with their ratings and traditional reasons.</param>
/// <param name="BestWindows">Longest daytime stretches free of the tradition's caution and avoid periods.</param>
/// <param name="FavorableWindows">Favorable periods the tradition shows, e.g. Abhijit Muhurta, Amrit Kalam.</param>
/// <param name="SpiritualWindows">Periods suited to spiritual practice, e.g. Brahma Muhurta.</param>
/// <param name="CautionWindows">Periods to use with caution, e.g. Gulika Kalam.</param>
/// <param name="AvoidWindows">Periods traditionally avoided for new beginnings, e.g. Rahu Kalam, Varjyam, Durmuhurtham.</param>
/// <param name="MixedWindows">Stretches where a favorable period overlaps a caution or avoid period, e.g. Amrit Kalam with Rahu Kalam.</param>
/// <param name="Methodology">Localized note explaining what this guidance is and is not.</param>
/// <param name="RulesVersion">Version of the guidance rules that produced this result.</param>
public sealed record DailyGuidance(
    GuidanceRating Rating,
    string RatingName,
    string Icon,
    string Headline,
    string Summary,
    IReadOnlyList<GuidanceFactor> Factors,
    IReadOnlyList<GuidanceWindow> BestWindows,
    IReadOnlyList<PanchangTimePeriod> FavorableWindows,
    IReadOnlyList<PanchangTimePeriod> SpiritualWindows,
    IReadOnlyList<PanchangTimePeriod> CautionWindows,
    IReadOnlyList<PanchangTimePeriod> AvoidWindows,
    IReadOnlyList<GuidanceMixedWindow> MixedWindows,
    string Methodology,
    string RulesVersion);

/// <summary>One Panchanga limb's contribution to the day's guidance.</summary>
/// <param name="Limb">Stable limb key: <c>tithi</c>, <c>vara</c>, <c>nakshatra</c>, <c>yoga</c> or <c>karana</c>.</param>
/// <param name="LimbName">Localized limb name, e.g. "Nakshatra".</param>
/// <param name="Key">Value rated, e.g. <c>Rohini</c>: the value in force for most of the daytime (or at sunrise, per the rules).</param>
/// <param name="Name">Localized value name.</param>
/// <param name="Rating">Rating of the value.</param>
/// <param name="RatingName">Localized rating label.</param>
/// <param name="Reason">Localized explanation, e.g. "Nakshatra Rohini: Highly Favorable. A fixed (Dhruva) Nakshatra…".</param>
public sealed record GuidanceFactor(
    string Limb, string LimbName, string Key, string Name, GuidanceRating Rating, string RatingName, string Reason);

/// <summary>A recommended time window.</summary>
public sealed record GuidanceWindow(DateTimeOffset Start, DateTimeOffset End, string StartDisplay, string EndDisplay);

/// <summary>
/// A stretch where at least one favorable period overlaps at least one caution or avoid period, e.g. Amrit Kalam (a
/// Nakshatra-based favorable interval) with Rahu Kalam (a weekday/daylight-based interval avoided for new beginnings).
/// The periods come from different Panchanga rules and neither cancels the other, so the stretch is Mixed. The
/// overlapping periods keep their own full start and end.
/// </summary>
/// <param name="Start">Start of the overlap.</param>
/// <param name="End">End of the overlap.</param>
/// <param name="StartDisplay"><paramref name="Start"/> in the location's display format.</param>
/// <param name="EndDisplay"><paramref name="End"/> in the location's display format.</param>
/// <param name="Rating">Always <see cref="GuidanceRating.Mixed"/>.</param>
/// <param name="RatingName">Localized rating label, e.g. "Mixed".</param>
/// <param name="Icon">Rating indicator, e.g. 🟡.</param>
/// <param name="FavorablePeriods">Favorable periods in force during the overlap, e.g. Amrit Kalam.</param>
/// <param name="UnfavorablePeriods">Caution or avoid periods in force during the overlap, e.g. Rahu Kalam.</param>
public sealed record GuidanceMixedWindow(
    DateTimeOffset Start,
    DateTimeOffset End,
    string StartDisplay,
    string EndDisplay,
    GuidanceRating Rating,
    string RatingName,
    string Icon,
    IReadOnlyList<PanchangTimePeriod> FavorablePeriods,
    IReadOnlyList<PanchangTimePeriod> UnfavorablePeriods);
