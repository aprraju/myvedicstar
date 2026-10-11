using MyVedicDay.Api.Contracts.Locations;
using MyVedicDay.Api.Contracts.Panchang;

namespace MyVedicDay.Api.Contracts.Muhurta;

/// <summary>The 27 birth Nakshatra choices in astronomical order.</summary>
public sealed record MuhurtaNakshatrasResponse(string Language, IReadOnlyList<MuhurtaNakshatraResponse> Nakshatras);

/// <summary>Pass Key as nakshatra; Name is localized and Order runs from 1 to 27.</summary>
public sealed record MuhurtaNakshatraResponse(string Key, string Name, int Order);

/// <summary>The activities for the "What are you planning?" cards, in display order.</summary>
/// <param name="Language">Language of names and descriptions.</param>
/// <param name="RulesVersion">Version of the Muhurta rules.</param>
/// <param name="DefaultActivity">Activity selected by default (<c>general</c>).</param>
/// <param name="Activities">Activities in display order. Render cards from this list; unknown <c>icon</c> keys fall back to a generic icon.</param>
public sealed record MuhurtaActivitiesResponse(string Language, string RulesVersion, string DefaultActivity, IReadOnlyList<MuhurtaActivityResponse> Activities);

/// <summary>An activity card.</summary>
/// <param name="Key">Activity key to pass as <c>activity</c>.</param>
/// <param name="Name">Localized name, e.g. "Puja / Spiritual".</param>
/// <param name="Description">Localized description.</param>
/// <param name="Icon">Icon key, e.g. <c>briefcase</c>, <c>plane</c>, <c>rings</c>.</param>
/// <param name="Order">Display order, from 1.</param>
/// <param name="RequiresInput">The user must describe the activity (<c>other</c>); send it as <c>otherText</c>.</param>
/// <param name="Completeness"><c>full</c>, or <c>partial</c> when a complete Muhurta needs factors this guidance does not include (Wedding, Housewarming).</param>
public sealed record MuhurtaActivityResponse(string Key, string Name, string? Description, string Icon, int Order, bool RequiresInput, string Completeness);

/// <summary>A status as the whole app uses it.</summary>
/// <param name="Status"><c>HighlyFavorable</c>, <c>Favorable</c>, <c>Mixed</c>, <c>Caution</c> or <c>Avoid</c>. Branch on this, never on <paramref name="Name"/>.</param>
/// <param name="Name">Localized label.</param>
/// <param name="Icon">Indicator, e.g. 🟢.</param>
public sealed record MuhurtaStatusResponse(string Status, string Name, string? Icon);

/// <summary>The activity that was evaluated.</summary>
/// <param name="Key">Activity evaluated.</param>
/// <param name="Name">Localized name.</param>
/// <param name="Icon">Icon key.</param>
/// <param name="Requested">Activity requested; <c>other</c> when <paramref name="RequestedText"/> was mapped to <paramref name="Key"/>.</param>
/// <param name="RequestedText">The description sent with <c>activity=other</c>.</param>
/// <param name="Completeness"><c>full</c> or <c>partial</c>.</param>
public sealed record EvaluatedActivityResponse(string Key, string Name, string Icon, string Requested, string? RequestedText, string Completeness);

/// <summary>The tradition applied.</summary>
public sealed record MuhurtaTraditionResponse(string Key, string Name);

/// <summary>A reason for a segment's status ("Why this time?").</summary>
/// <param name="Type"><c>positive</c>, <c>negative</c> or <c>info</c>.</param>
/// <param name="Code">Stable code, e.g. <c>FAVORABLE_NAKSHATRA</c>, <c>AMRIT_KALAM</c>, <c>NO_RAHU_KALAM_CONFLICT</c>.</param>
/// <param name="Label">Localized label, e.g. "Favorable Nakshatra".</param>
/// <param name="Subject">Value or period behind it, e.g. <c>Rohini</c> or <c>AmritKalam</c>.</param>
public sealed record GoodTimeReasonResponse(string Type, string Code, string Label, string? Subject);

/// <summary>An unfavorable period overlapping a segment.</summary>
/// <param name="Code">Stable code, e.g. <c>RAHU_KALAM</c>.</param>
/// <param name="Kind">Period kind, e.g. <c>RahuKalam</c> (as in Panchang responses).</param>
/// <param name="Severity">Best status the period allows here: from the conflict matrix, alone or when overlapping a favorable period.</param>
/// <param name="Label">Localized label, e.g. "Overlaps Rahu Kalam".</param>
/// <param name="WithFavorable">Whether it overlaps a favorable period in this segment.</param>
/// <param name="Start">Start of the whole unfavorable period.</param>
/// <param name="End">End of the whole unfavorable period.</param>
/// <param name="StartDisplay"><paramref name="Start"/> in the location's display format.</param>
/// <param name="EndDisplay"><paramref name="End"/> in the location's display format.</param>
/// <param name="OverlapStart">Start of the part inside the segment.</param>
/// <param name="OverlapEnd">End of the part inside the segment.</param>
/// <param name="OverlapStartDisplay"><paramref name="OverlapStart"/> in the location's display format.</param>
/// <param name="OverlapEndDisplay"><paramref name="OverlapEnd"/> in the location's display format.</param>
public sealed record GoodTimeConflictResponse(
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

/// <summary>A stretch of the day with one resolved status.</summary>
/// <param name="Id">Segment id within the response, e.g. <c>s4</c>.</param>
/// <param name="Start">Segment start.</param>
/// <param name="End">Segment end.</param>
/// <param name="StartDisplay"><paramref name="Start"/> in the location's display format.</param>
/// <param name="EndDisplay"><paramref name="End"/> in the location's display format.</param>
/// <param name="TimeRangeDisplay">Short range, e.g. <c>16:27–17:16 CDT</c>.</param>
/// <param name="Status">Resolved status.</param>
/// <param name="Score">0–100, for ordering only; not a percentage to display.</param>
/// <param name="IsGoodTime">True only for Highly Favorable or Favorable segments without conflicts that are long enough. The only flag a client may show as a Good Time.</param>
/// <param name="Headline">Localized headline, e.g. "Favorable for Business".</param>
/// <param name="Reasons">Why: positive, negative and informational reasons.</param>
/// <param name="Conflicts">Unfavorable periods active in the segment; empty for every Good Time.</param>
public sealed record GoodTimeSegmentResponse(
    string Id,
    DateTimeOffset Start,
    DateTimeOffset End,
    string StartDisplay,
    string EndDisplay,
    string TimeRangeDisplay,
    MuhurtaStatusResponse Status,
    int Score,
    bool IsGoodTime,
    string Headline,
    IReadOnlyList<GoodTimeReasonResponse> Reasons,
    IReadOnlyList<GoodTimeConflictResponse> Conflicts);

/// <summary>One day evaluated for one activity.</summary>
/// <param name="Location">Location actually used.</param>
/// <param name="Tradition">Tradition applied.</param>
/// <param name="Activity">Activity evaluated.</param>
/// <param name="Date">Local date evaluated.</param>
/// <param name="DateDisplay"><paramref name="Date"/> in the location's date format.</param>
/// <param name="LocalToday">Current local date at the location.</param>
/// <param name="DisplayFormat">Format of every <c>…Display</c> value.</param>
/// <param name="GeneratedAt">UTC time the response was generated.</param>
/// <param name="Language">Language of labels.</param>
/// <param name="RulesVersion">Version of the Muhurta rules (provisional).</param>
/// <param name="CalculationVersion">Version of the evaluation algorithm.</param>
/// <param name="DayStatus">The best Good Time's status; without a Good Time, at best <c>Mixed</c>. Same value as the calendar tile.</param>
/// <param name="Segments">The whole evaluation window in time order: daytime, or sunrise to next sunrise for activities such as Travel and Puja.</param>
/// <param name="GoodTimes">Segment ids of the Good Times, best first. Empty when none qualifies: no other segment is ever promoted.</param>
/// <param name="NoGoodTimeReason"><c>NO_QUALIFYING_PERIOD</c> or <c>DAY_EXCLUDED_FOR_ACTIVITY</c> when <paramref name="GoodTimes"/> is empty.</param>
/// <param name="NoGoodTimeMessage">Localized message for that case.</param>
/// <param name="NowSegmentId">Segment in progress now at the location, when <paramref name="Date"/> is today.</param>
/// <param name="NextGoodTimeId">Next Good Time starting later today, when <paramref name="Date"/> is today.</param>
/// <param name="Notices">Localized notices, e.g. the activity "Other" was mapped to, or partial rules.</param>
/// <param name="Disclaimer">Informational notice to show below the results.</param>
public sealed record GoodTimesResponse(
    LocationResponse Location,
    MuhurtaTraditionResponse Tradition,
    EvaluatedActivityResponse Activity,
    DateOnly Date,
    string DateDisplay,
    DateOnly LocalToday,
    DisplayFormatResponse DisplayFormat,
    DateTimeOffset GeneratedAt,
    string Language,
    string RulesVersion,
    string CalculationVersion,
    MuhurtaStatusResponse DayStatus,
    IReadOnlyList<GoodTimeSegmentResponse> Segments,
    IReadOnlyList<string> GoodTimes,
    string? NoGoodTimeReason,
    string? NoGoodTimeMessage,
    string? NowSegmentId,
    string? NextGoodTimeId,
    IReadOnlyList<string> Notices,
    string Disclaimer);

/// <summary>One "Why this time?" note.</summary>
/// <param name="SegmentId">Segment the note belongs to (an <c>id</c> from <c>good-times</c>).</param>
/// <param name="Text">The note.</param>
public sealed record GoodTimeNoteResponse(string SegmentId, string Text);

/// <summary>
/// The "Why this time?" narrative for a day evaluation. It never changes statuses, times or Good Times: render those
/// from <c>good-times</c> and use this text only as an explanation.
/// </summary>
/// <param name="Activity">Activity evaluated.</param>
/// <param name="Date">Local date evaluated.</param>
/// <param name="Language">Language of the text.</param>
/// <param name="RulesVersion">Version of the Muhurta rules.</param>
/// <param name="Source"><c>ai</c> when written by the configured AI provider and validated against the evaluation; otherwise <c>template</c>.</param>
/// <param name="Summary">A few sentences about the day for the activity.</param>
/// <param name="Notes">Notes for Good Times and for favorable periods with conflicts.</param>
/// <param name="Provider">AI provider, when <paramref name="Source"/> is <c>ai</c>.</param>
/// <param name="Model">AI model, when <paramref name="Source"/> is <c>ai</c>.</param>
/// <param name="PromptVersion">Version of the AI instructions.</param>
/// <param name="FallbackReason">Why AI text was not used, e.g. <c>TimedOut</c> or <c>UNKNOWN_TIME</c>; null when it was, or when AI is off.</param>
/// <param name="Disclaimer">Informational notice to show with the explanation.</param>
public sealed record GoodTimeExplanationResponse(
    EvaluatedActivityResponse Activity,
    DateOnly Date,
    string Language,
    string RulesVersion,
    string Source,
    string Summary,
    IReadOnlyList<GoodTimeNoteResponse> Notes,
    string? Provider,
    string? Model,
    string PromptVersion,
    string? FallbackReason,
    string Disclaimer);

/// <summary>A Good Time window.</summary>
public sealed record GoodTimeWindowResponse(DateTimeOffset Start, DateTimeOffset End, string StartDisplay, string EndDisplay, MuhurtaStatusResponse Status);

/// <summary>One day's resolved status, for calendar tiles and the Home card.</summary>
/// <param name="Date">Local date.</param>
/// <param name="DateDisplay"><paramref name="Date"/> in the location's date format.</param>
/// <param name="Status">The day status (same as <c>dayStatus</c> of <c>good-times</c>).</param>
/// <param name="GoodTimeCount">Number of Good Times.</param>
/// <param name="BestGoodTime">The highest-ranked Good Time; null when none.</param>
/// <param name="NoGoodTimeReason">Why there is no Good Time; null when there is one.</param>
public sealed record MuhurtaDaySummaryResponse(
    DateOnly Date, string DateDisplay, MuhurtaStatusResponse Status, int GoodTimeCount, GoodTimeWindowResponse? BestGoodTime, string? NoGoodTimeReason);

/// <summary>Day summaries for a range of dates.</summary>
public sealed record DaySummariesResponse(
    LocationResponse Location,
    MuhurtaTraditionResponse Tradition,
    EvaluatedActivityResponse Activity,
    DateOnly LocalToday,
    DisplayFormatResponse DisplayFormat,
    string Language,
    string RulesVersion,
    IReadOnlyList<MuhurtaDaySummaryResponse> Days);

/// <summary>One of the next best Good Times.</summary>
/// <param name="Rank">1 for the best option.</param>
/// <param name="Date">Local date of the option.</param>
/// <param name="DateDisplay"><paramref name="Date"/> in the location's date format.</param>
/// <param name="Segment">The Good Time, exactly as <c>good-times</c> returns it for <paramref name="Date"/> (same <c>id</c>).</param>
public sealed record BestGoodTimeResponse(int Rank, DateOnly Date, string DateDisplay, GoodTimeSegmentResponse Segment);

/// <summary>The next best Good Times for an activity, at most one per day.</summary>
/// <param name="Location">The resolved location.</param>
/// <param name="Tradition">The tradition applied.</param>
/// <param name="Activity">The activity evaluated.</param>
/// <param name="LocalToday">Current local date at the location.</param>
/// <param name="DisplayFormat">Format of every <c>…Display</c> value.</param>
/// <param name="GeneratedAt">UTC time the response was generated.</param>
/// <param name="Language">Language of labels.</param>
/// <param name="RulesVersion">Version of the Muhurta rules (provisional).</param>
/// <param name="CalculationVersion">Version of the evaluation algorithm.</param>
/// <param name="SearchedFrom">First local date searched (today at the location).</param>
/// <param name="SearchedThrough">Last local date searched.</param>
/// <param name="Options">Best first: Highly Favorable before Favorable, then soonest. Only Good Times; empty when none qualifies.</param>
/// <param name="NoGoodTimeMessage">Localized message when <paramref name="Options"/> is empty.</param>
/// <param name="Notices">Localized notices, e.g. the activity "Other" was mapped to, or partial rules.</param>
/// <param name="Disclaimer">Informational notice to show with the options.</param>
public sealed record BestGoodTimesResponse(
    LocationResponse Location,
    MuhurtaTraditionResponse Tradition,
    EvaluatedActivityResponse Activity,
    DateOnly LocalToday,
    DisplayFormatResponse DisplayFormat,
    DateTimeOffset GeneratedAt,
    string Language,
    string RulesVersion,
    string CalculationVersion,
    DateOnly SearchedFrom,
    DateOnly SearchedThrough,
    IReadOnlyList<BestGoodTimeResponse> Options,
    string? NoGoodTimeMessage,
    IReadOnlyList<string> Notices,
    string Disclaimer);
