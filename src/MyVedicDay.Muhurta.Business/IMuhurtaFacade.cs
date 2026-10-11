using System.ComponentModel.DataAnnotations;
using MyVedicDay.Localization;
using MyVedicDay.Location;

namespace MyVedicDay.Muhurta.Business;

/// <summary>Muhurta settings. Bound from the <c>Muhurta</c> configuration section and validated at startup.</summary>
public sealed class MuhurtaOptions
{
    public const string SectionName = "Muhurta";

    /// <summary>Activity used when a request does not name one; also the activity of the calendar and Home summaries.</summary>
    [Required]
    public string DefaultActivity { get; set; } = "general";

    /// <summary>Largest number of days a day-summary request may cover (a year view).</summary>
    [Range(1, 400)]
    public int MaxRangeDays { get; set; } = 366;

    /// <summary>Lowest AI confidence at which "Other" text is mapped to the activity the model chose.</summary>
    [Range(0.0, 1.0)]
    public double OtherTextMinimumConfidence { get; set; } = 0.6;

    /// <summary>Longest "Other" description accepted.</summary>
    [Range(10, 200)]
    public int OtherTextMaxLength { get; set; } = 80;

    /// <summary>Days searched for the next best Good Times when a request does not say.</summary>
    [Range(1, 90)]
    public int BestSearchDefaultDays { get; set; } = 30;

    /// <summary>Largest number of days a next-best-Good-Times request may search.</summary>
    [Range(1, 366)]
    public int BestSearchMaxDays { get; set; } = 90;

    /// <summary>Number of next best Good Times returned when a request does not say.</summary>
    [Range(1, 20)]
    public int BestDefaultCount { get; set; } = 3;

    /// <summary>Largest number of next best Good Times a request may ask for.</summary>
    [Range(1, 20)]
    public int BestMaxCount { get; set; } = 10;

    /// <summary>Cache lifetime for dates that have already ended at the location.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan PastDateCacheLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Upper bound on any cache entry's lifetime.</summary>
    [Range(typeof(TimeSpan), "01:00:00", "400.00:00:00")]
    public TimeSpan MaxCacheLifetime { get; set; } = TimeSpan.FromDays(35);
}

/// <summary>Where, for which tradition and in which language to evaluate.</summary>
public sealed record MuhurtaQuery(LocationQuery Location, string? Tradition = null, IReadOnlyList<string>? PreferredLanguages = null, string? Nakshatra = null);

/// <summary>What to evaluate.</summary>
/// <param name="Date">Local date; the location's local today when omitted.</param>
/// <param name="Activity">Activity key, e.g. <c>business</c>; the default activity when omitted.</param>
/// <param name="OtherText">Free-text description, required when <paramref name="Activity"/> is <c>other</c>.</param>
public sealed record GoodTimesRequest(DateOnly? Date = null, string? Activity = null, string? OtherText = null);

/// <summary>What to search for in "Next best times".</summary>
/// <param name="Activity">Activity key; the default activity when omitted.</param>
/// <param name="OtherText">Free-text description, required when <paramref name="Activity"/> is <c>other</c>.</param>
/// <param name="Count">Number of options to return; the configured default when omitted.</param>
/// <param name="Days">Days to search from the location's today; the configured default when omitted.</param>
public sealed record BestGoodTimesRequest(string? Activity = null, string? OtherText = null, int? Count = null, int? Days = null);

/// <summary>The activities a user can choose, in display order.</summary>
public sealed record MuhurtaActivitiesResult(string Language, string RulesVersion, string DefaultActivity, IReadOnlyList<MuhurtaActivity> Activities);

/// <summary>Canonical birth-star keys and localized names, in astronomical order.</summary>
public sealed record MuhurtaNakshatrasResult(string Language, IReadOnlyList<MuhurtaNakshatra> Nakshatras);

/// <summary>A birth-star key accepted as nakshatra, localized name and one-based order.</summary>
public sealed record MuhurtaNakshatra(string Key, string Name, int Order);

/// <summary>An activity card.</summary>
/// <param name="Key">Stable key, e.g. <c>business</c>.</param>
/// <param name="Name">Localized name.</param>
/// <param name="Description">Localized description.</param>
/// <param name="Icon">Icon key for clients, e.g. <c>briefcase</c>.</param>
/// <param name="Order">Display order.</param>
/// <param name="RequiresInput">The user must describe the activity (<c>other</c>).</param>
/// <param name="Completeness"><c>full</c>, or <c>partial</c> when factors the engine cannot evaluate yet matter.</param>
public sealed record MuhurtaActivity(string Key, string Name, string? Description, string Icon, int Order, bool RequiresInput, string Completeness);

/// <summary>A tradition applied to an evaluation.</summary>
public sealed record MuhurtaTradition(string Key, string Name);

/// <summary>The activity that was evaluated.</summary>
/// <param name="Key">Activity evaluated, e.g. <c>business</c>.</param>
/// <param name="Name">Localized name.</param>
/// <param name="Icon">Icon key.</param>
/// <param name="Requested">Activity requested; <c>other</c> when free text was mapped to <paramref name="Key"/>.</param>
/// <param name="RequestedText">The free text, when <paramref name="Requested"/> is <c>other</c>.</param>
/// <param name="Completeness"><c>full</c> or <c>partial</c>.</param>
public sealed record EvaluatedActivity(string Key, string Name, string Icon, string Requested, string? RequestedText, string Completeness);

/// <summary>A status with its localized label and icon.</summary>
/// <param name="Status"><c>HighlyFavorable</c>, <c>Favorable</c>, <c>Mixed</c>, <c>Caution</c> or <c>Avoid</c>.</param>
/// <param name="Name">Localized label.</param>
/// <param name="Icon">Indicator, e.g. 🟢.</param>
public sealed record MuhurtaStatusInfo(string Status, string Name, string? Icon);

/// <summary>A reason for a segment's status.</summary>
/// <param name="Type"><c>positive</c>, <c>negative</c> or <c>info</c>.</param>
/// <param name="Code">Stable code, e.g. <c>FAVORABLE_NAKSHATRA</c>.</param>
/// <param name="Label">Localized label, e.g. "Favorable Nakshatra".</param>
/// <param name="Subject">Value or period behind it, e.g. <c>Rohini</c>; null when none.</param>
public sealed record GoodTimeReason(string Type, string Code, string Label, string? Subject);

/// <summary>An unfavorable period overlapping a segment.</summary>
public sealed record GoodTimeConflict(
    string Code,
    string Kind,
    string Severity,
    string Label,
    bool WithFavorable,
    DateTimeOffset Start,
    DateTimeOffset End,
    string StartDisplay,
    string EndDisplay,
    DateTimeOffset OverlapStart,
    DateTimeOffset OverlapEnd,
    string OverlapStartDisplay,
    string OverlapEndDisplay);

/// <summary>A segment of the evaluated day with its resolved status.</summary>
public sealed record GoodTimeSegment(
    string Id,
    DateTimeOffset Start,
    DateTimeOffset End,
    string StartDisplay,
    string EndDisplay,
    string TimeRangeDisplay,
    MuhurtaStatusInfo Status,
    int Score,
    bool IsGoodTime,
    string Headline,
    IReadOnlyList<GoodTimeReason> Reasons,
    IReadOnlyList<GoodTimeConflict> Conflicts);

/// <summary>A day evaluated for an activity.</summary>
/// <param name="GoodTimes">Ids of the Good Time segments, best first. Empty when none qualifies: nothing is promoted.</param>
/// <param name="NowSegmentId">Segment in progress at the location, when the date is today.</param>
/// <param name="NextGoodTimeId">Next Good Time starting later today, when the date is today.</param>
/// <param name="Notices">Localized notices, e.g. partial rules or the activity "Other" was mapped to.</param>
public sealed record GoodTimesResult(
    LocationDetails Location,
    MuhurtaTradition Tradition,
    EvaluatedActivity Activity,
    DateOnly Date,
    string DateDisplay,
    DateOnly LocalToday,
    DateTimeDisplayFormat DisplayFormat,
    DateTimeOffset GeneratedAt,
    string Language,
    string RulesVersion,
    string CalculationVersion,
    MuhurtaStatusInfo DayStatus,
    IReadOnlyList<GoodTimeSegment> Segments,
    IReadOnlyList<string> GoodTimes,
    string? NoGoodTimeReason,
    string? NoGoodTimeMessage,
    string? NowSegmentId,
    string? NextGoodTimeId,
    IReadOnlyList<string> Notices,
    string Disclaimer);

/// <summary>A Good Time window in a summary.</summary>
public sealed record GoodTimeWindow(DateTimeOffset Start, DateTimeOffset End, string StartDisplay, string EndDisplay, MuhurtaStatusInfo Status);

/// <summary>One day's resolved status for calendar tiles and Home.</summary>
/// <param name="Status">The best Good Time's status; without one, at best Mixed.</param>
/// <param name="BestGoodTime">The highest-ranked Good Time; null when none.</param>
public sealed record MuhurtaDaySummary(
    DateOnly Date, string DateDisplay, MuhurtaStatusInfo Status, int GoodTimeCount, GoodTimeWindow? BestGoodTime, string? NoGoodTimeReason);

/// <summary>Day summaries for a range of dates.</summary>
public sealed record DaySummariesResult(
    LocationDetails Location,
    MuhurtaTradition Tradition,
    EvaluatedActivity Activity,
    DateOnly LocalToday,
    DateTimeDisplayFormat DisplayFormat,
    string Language,
    string RulesVersion,
    IReadOnlyList<MuhurtaDaySummary> Days);

/// <summary>One of the next best Good Times.</summary>
/// <param name="Rank">1 for the best option.</param>
/// <param name="Date">Local date of the option.</param>
/// <param name="DateDisplay">Date formatted for the location.</param>
/// <param name="Segment">The Good Time, exactly as <c>good-times</c> returns it for that date.</param>
public sealed record BestGoodTime(int Rank, DateOnly Date, string DateDisplay, GoodTimeSegment Segment);

/// <summary>The next best Good Times for an activity, at most one per day.</summary>
/// <param name="Options">Best first: Highly Favorable before Favorable, then soonest. Empty when none qualifies.</param>
/// <param name="NoGoodTimeMessage">Present when <paramref name="Options"/> is empty.</param>
public sealed record BestGoodTimesResult(
    LocationDetails Location,
    MuhurtaTradition Tradition,
    EvaluatedActivity Activity,
    DateOnly LocalToday,
    DateTimeDisplayFormat DisplayFormat,
    DateTimeOffset GeneratedAt,
    string Language,
    string RulesVersion,
    string CalculationVersion,
    DateOnly SearchedFrom,
    DateOnly SearchedThrough,
    IReadOnlyList<BestGoodTime> Options,
    string? NoGoodTimeMessage,
    IReadOnlyList<string> Notices,
    string Disclaimer);

/// <summary>One "Why this time?" note for a segment.</summary>
public sealed record GoodTimeNote(string SegmentId, string Text);

/// <summary>
/// The narrative explanation of an evaluation (ADR-0012). It never changes statuses or times: those come from
/// <see cref="GoodTimesResult"/>.
/// </summary>
/// <param name="Source"><c>ai</c> when generated and validated, otherwise <c>template</c> (deterministic).</param>
/// <param name="Summary">A few sentences about the day for the activity.</param>
/// <param name="Notes">Notes for Good Times and for favorable periods with conflicts.</param>
/// <param name="Provider">AI provider used, when <paramref name="Source"/> is <c>ai</c>.</param>
/// <param name="Model">AI model used, when <paramref name="Source"/> is <c>ai</c>.</param>
/// <param name="PromptVersion">Version of the AI instructions.</param>
/// <param name="FallbackReason">Why the AI text was not used (e.g. <c>TimedOut</c>, <c>UNKNOWN_TIME</c>); null otherwise.</param>
public sealed record GoodTimeExplanation(
    string Source,
    string Summary,
    IReadOnlyList<GoodTimeNote> Notes,
    string? Provider,
    string? Model,
    string PromptVersion,
    string? FallbackReason);

/// <summary>An explanation together with what it explains.</summary>
public sealed record GoodTimeExplanationResult(
    EvaluatedActivity Activity, DateOnly Date, string Language, string RulesVersion, GoodTimeExplanation Explanation, string Disclaimer);

/// <summary>
/// Public entry point to Find a Good Time (ADR-0011). Every status, time and Good Time comes from the rules engine;
/// nothing is promoted when no period qualifies.
/// </summary>
/// <remarks>
/// Throws <see cref="MyVedicDay.Shared.Exceptions.ValidationException"/> for an invalid location, tradition, activity,
/// date or description, and <see cref="MyVedicDay.Shared.Exceptions.BusinessRuleException"/> when the sun does not rise
/// or set.
/// </remarks>
public interface IMuhurtaFacade
{
    /// <summary>The activity catalog for the activity cards.</summary>
    Task<MuhurtaActivitiesResult> GetActivitiesAsync(string? tradition, IReadOnlyList<string>? preferredLanguages, CancellationToken cancellationToken);
    Task<MuhurtaNakshatrasResult> GetNakshatrasAsync(string? tradition, IReadOnlyList<string>? preferredLanguages, CancellationToken cancellationToken);

    /// <summary>One day evaluated for one activity.</summary>
    Task<GoodTimesResult> GetGoodTimesAsync(MuhurtaQuery query, GoodTimesRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The "Why this time?" narrative for the same evaluation as <see cref="GetGoodTimesAsync"/>: AI-written when enabled
    /// and valid, otherwise the deterministic template.
    /// </summary>
    Task<GoodTimeExplanationResult> GetExplanationAsync(MuhurtaQuery query, GoodTimesRequest request, CancellationToken cancellationToken);

    /// <summary>Day statuses for <paramref name="days"/> dates from <paramref name="startDate"/>.</summary>
    /// <param name="otherText">Free-text description, required when <paramref name="activity"/> is <c>other</c>.</param>
    Task<DaySummariesResult> GetDaySummariesAsync(
        MuhurtaQuery query, DateOnly startDate, int days, string? activity, string? otherText, CancellationToken cancellationToken);

    /// <summary>
    /// The next best Good Times from now: at most one per day (that day's highest-ranked Good Time that has not ended),
    /// Highly Favorable before Favorable, then soonest. Only Good Times are returned; nothing is promoted.
    /// </summary>
    Task<BestGoodTimesResult> GetBestGoodTimesAsync(MuhurtaQuery query, BestGoodTimesRequest request, CancellationToken cancellationToken);
}
