namespace MyVedicDay.Panchang.Data.Guidance;

/// <summary>
/// The versioned daily-guidance rules (<c>panchang-guidance-rules.json</c>): the application's own methodology for
/// summarizing the five Panchanga limbs into simple daily guidance. Not a universal traditional rule and not a Muhurta
/// calculation; interpretation only, no astronomy.
/// </summary>
/// <param name="Version">Rules version, returned with every result; bump it on every change.</param>
/// <param name="ValueSelection">
/// Which value of each limb is rated: <c>PredominantDaytime</c> (the value in force for most of sunrise→sunset) or
/// <c>Sunrise</c> (the value at sunrise, the formal Panchang-day convention).
/// </param>
/// <param name="RatingScores">Internal score (0–100) for each rating name, used only to combine limb ratings.</param>
/// <param name="Thresholds">Minimum combined score per day rating, evaluated highest first.</param>
/// <param name="BestWindows">How "best windows" (daytime free of caution/avoid periods) are selected.</param>
/// <param name="SpiritualPeriods">Period kinds reported as spiritual windows rather than general favorable windows.</param>
/// <param name="Default">Rules applied to every tradition.</param>
/// <param name="Traditions">Partial rule sets per tradition that replace individual weights or value ratings.</param>
public sealed record GuidanceRulesDocument(
    string Version,
    string? ValueSelection,
    IReadOnlyDictionary<string, int> RatingScores,
    IReadOnlyList<GuidanceThreshold> Thresholds,
    BestWindowRules BestWindows,
    IReadOnlyList<string> SpiritualPeriods,
    GuidanceRuleSet Default,
    IReadOnlyDictionary<string, GuidanceRuleSet>? Traditions);

/// <summary>Minimum combined score for a day rating.</summary>
public sealed record GuidanceThreshold(string Rating, int MinScore);

/// <summary>Best-window selection: free daytime segments at least <paramref name="MinimumMinutes"/> long, longest first.</summary>
public sealed record BestWindowRules(int MinimumMinutes, int MaximumCount);

/// <summary>
/// Limb weights and a rating per value. Weight keys are limb names (<c>tithi</c>, <c>nakshatra</c>, <c>yoga</c>,
/// <c>karana</c>, <c>vara</c>); value keys are element keys (e.g. <c>ShuklaPanchami</c>, <c>Rohini</c>, <c>Thursday</c>).
/// </summary>
public sealed record GuidanceRuleSet(
    IReadOnlyDictionary<string, double>? Weights,
    IReadOnlyDictionary<string, GuidanceValueRule>? Tithi,
    IReadOnlyDictionary<string, GuidanceValueRule>? Nakshatra,
    IReadOnlyDictionary<string, GuidanceValueRule>? Yoga,
    IReadOnlyDictionary<string, GuidanceValueRule>? Karana,
    IReadOnlyDictionary<string, GuidanceValueRule>? Vara);

/// <summary>Rating of one value, with the traditional group that explains it (e.g. <c>rikta</c>, <c>dhruva</c>).</summary>
public sealed record GuidanceValueRule(string Rating, string? Group);

/// <summary>
/// Source of the daily-guidance rules. The embedded file can be replaced as a whole by
/// <c>panchang-guidance-rules.json</c> in the content directory.
/// </summary>
public interface IGuidanceRulesRepository
{
    /// <exception cref="MyVedicDay.Shared.Exceptions.DataAccessException">The rules could not be read.</exception>
    Task<GuidanceRulesDocument> GetRulesAsync(CancellationToken cancellationToken);
}
