using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Muhurta.Calculation.Models;

/// <summary>Input for evaluating one local date for one activity.</summary>
public sealed record MuhurtaCalculationRequest(LocalDate Date, GeoCoordinate Location, DateTimeZone TimeZone, ActivityRuleProfile Profile, Nakshatra? BirthNakshatra = null);

/// <summary>Whether a reason supports or weighs against a segment.</summary>
public enum ReasonType
{
    Positive = 1,
    Negative,
    Info,
}

/// <summary>Why a segment has its status.</summary>
/// <param name="Type">Positive, negative or informational.</param>
/// <param name="Code">Stable UPPER_SNAKE code, e.g. <c>FAVORABLE_NAKSHATRA</c>, <c>AMRIT_KALAM</c>, <c>NO_RAHU_KALAM_CONFLICT</c>.</param>
/// <param name="Subject">The value or period behind it, e.g. <c>Rohini</c> or <c>AmritKalam</c>; null when none.</param>
public sealed record MuhurtaReason(ReasonType Type, string Code, string? Subject = null);

/// <summary>An unfavorable period active during a segment.</summary>
/// <param name="Kind">Period kind, e.g. <see cref="PanchangPeriodKind.RahuKalam"/>.</param>
/// <param name="Code">Stable code, e.g. <c>RAHU_KALAM</c>.</param>
/// <param name="Severity">Best status the period allows here (from the conflict matrix).</param>
/// <param name="WithFavorable">Whether it overlaps a favorable period in this segment.</param>
/// <param name="PeriodStart">Start of the whole unfavorable period.</param>
/// <param name="PeriodEnd">End of the whole unfavorable period.</param>
/// <param name="OverlapStart">Start of the part inside this segment.</param>
/// <param name="OverlapEnd">End of the part inside this segment.</param>
public sealed record MuhurtaConflict(
    PanchangPeriodKind Kind,
    string Code,
    MuhurtaStatus Severity,
    bool WithFavorable,
    Instant PeriodStart,
    Instant PeriodEnd,
    Instant OverlapStart,
    Instant OverlapEnd);

/// <summary>A stretch of the day with one resolved status.</summary>
/// <param name="Start">Segment start.</param>
/// <param name="End">Segment end.</param>
/// <param name="Status">Resolved status after conflicts and caps.</param>
/// <param name="Score">0–100, for ordering only (not a percentage to display).</param>
/// <param name="IsGoodTime">Qualifies as a Good Time: Highly Favorable or Favorable, no conflict, long enough, inside the window.</param>
/// <param name="Reasons">Positive, negative and informational reasons.</param>
/// <param name="Conflicts">Unfavorable periods active in the segment.</param>
public sealed record MuhurtaSegment(
    Instant Start,
    Instant End,
    MuhurtaStatus Status,
    int Score,
    bool IsGoodTime,
    IReadOnlyList<MuhurtaReason> Reasons,
    IReadOnlyList<MuhurtaConflict> Conflicts);

/// <summary>The evaluation of one day for one activity.</summary>
/// <param name="Date">Local date.</param>
/// <param name="DayStatus">The best Good Time's status; without one, at best Mixed.</param>
/// <param name="Segments">Every segment of the evaluation window, in time order.</param>
/// <param name="GoodTimes">Indexes into <paramref name="Segments"/> of the Good Times, best first.</param>
/// <param name="NoGoodTimeReason">Why there is no Good Time (<see cref="NoGoodTimeReasons"/>); null when there is one.</param>
/// <param name="LunarMonth">The Amanta lunar month at sunrise, when the profile restricts months.</param>
public sealed record MuhurtaEvaluation(
    LocalDate Date,
    MuhurtaStatus DayStatus,
    IReadOnlyList<MuhurtaSegment> Segments,
    IReadOnlyList<int> GoodTimes,
    string? NoGoodTimeReason,
    LunarMonth? LunarMonth);

/// <summary>Codes explaining a day without a Good Time.</summary>
public static class NoGoodTimeReasons
{
    /// <summary>Every segment falls short or conflicts.</summary>
    public const string NoQualifyingPeriod = "NO_QUALIFYING_PERIOD";

    /// <summary>The day is ruled out for the activity (month, Adhika month, or an excluded value all day).</summary>
    public const string DayExcludedForActivity = "DAY_EXCLUDED_FOR_ACTIVITY";
}
