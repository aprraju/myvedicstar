using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Muhurta.Calculation.Models;

/// <summary>Resolved status of a time segment or a day, from most to least favorable. Ordered: a larger value is worse.</summary>
public enum MuhurtaStatus
{
    HighlyFavorable = 1,
    Favorable,
    Mixed,
    Caution,
    Avoid,
}

/// <summary>How a Panchanga value affects an activity.</summary>
public enum FactorEffect
{
    Supportive = 1,
    Neutral,
    Adverse,

    /// <summary>Rules the activity out while the value is in force (e.g. Vishti Karana for a wedding).</summary>
    Excluded,
}

/// <summary>A Panchanga limb.</summary>
public enum Limb
{
    Tithi = 1,
    Vara,
    Nakshatra,
    Yoga,
    Karana,
}

/// <summary>Part of the Panchang day an activity is evaluated in.</summary>
public enum EvaluationWindow
{
    /// <summary>Sunrise to sunset.</summary>
    Daytime = 1,

    /// <summary>Sunrise to the next sunrise.</summary>
    Any,
}

/// <summary>A favorable period's contribution: a score bonus, except on weekdays where tradition does not use it.</summary>
/// <param name="Bonus">Points added to the segment score (0–100 scale); the largest active bonus counts.</param>
/// <param name="ExcludedWeekdays">Weekdays the period is not favorable, e.g. Abhijit Muhurta on Wednesday.</param>
public sealed record FavorablePeriodRule(int Bonus, IReadOnlySet<IsoDayOfWeek> ExcludedWeekdays);

/// <summary>
/// An unfavorable period's severity: the best status a segment can have while the period is active, alone or when it
/// overlaps a favorable period (the conflict matrix).
/// </summary>
public sealed record UnfavorablePeriodRule(MuhurtaStatus Alone, MuhurtaStatus WithFavorable);

/// <summary>Minimum score for a status.</summary>
public sealed record StatusThreshold(MuhurtaStatus Status, int MinScore);

/// <summary>
/// The fully resolved rules for one activity in one tradition: defaults, activity overrides and tradition overrides
/// already merged. Pure data; the engine has no activity-specific code.
/// </summary>
/// <param name="Activity">Activity key, e.g. <c>business</c>.</param>
/// <param name="Window">Part of the day evaluated.</param>
/// <param name="MinimumMinutes">Shortest segment that can be a Good Time.</param>
/// <param name="MaximumStatus">Best status any segment may reach (e.g. Favorable for activities evaluated with partial rules).</param>
/// <param name="Weights">Weight of each limb in the segment score.</param>
/// <param name="Factors">Effect per limb and value key (e.g. <c>Nakshatra → Rohini → Supportive</c>); values not listed are Neutral.</param>
/// <param name="EffectScores">Score of each effect (0–100), combined by weight.</param>
/// <param name="Thresholds">Minimum score per status, evaluated best first.</param>
/// <param name="FavorablePeriods">Periods that support the activity.</param>
/// <param name="UnfavorablePeriods">Periods that conflict with it, with their severities.</param>
/// <param name="ExcludedLunarMonths">Amanta lunar months in which the activity is not undertaken.</param>
/// <param name="ExcludeAdhikaMonth">Whether an Adhika (intercalary) month rules the activity out.</param>
public sealed record ActivityRuleProfile(
    string Activity,
    EvaluationWindow Window,
    int MinimumMinutes,
    MuhurtaStatus? MaximumStatus,
    IReadOnlyDictionary<Limb, double> Weights,
    IReadOnlyDictionary<Limb, IReadOnlyDictionary<string, FactorEffect>> Factors,
    IReadOnlyDictionary<FactorEffect, int> EffectScores,
    IReadOnlyList<StatusThreshold> Thresholds,
    IReadOnlyDictionary<PanchangPeriodKind, FavorablePeriodRule> FavorablePeriods,
    IReadOnlyDictionary<PanchangPeriodKind, UnfavorablePeriodRule> UnfavorablePeriods,
    IReadOnlySet<LunarMonth> ExcludedLunarMonths,
    bool ExcludeAdhikaMonth);
