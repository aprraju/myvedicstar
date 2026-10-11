using MyVedicDay.Api.Contracts.Locations;
using MyVedicDay.Api.Contracts.Panchang;
using MyVedicDay.Localization;
using MyVedicDay.Muhurta.Business;

namespace MyVedicDay.Api.Contracts.Muhurta;

/// <summary>Maps Muhurta facade results to API response contracts. Statuses and flags pass through unchanged.</summary>
internal static class MuhurtaResponseMapper
{
    public static MuhurtaActivitiesResponse ToResponse(this MuhurtaActivitiesResult result) =>
        new(
            result.Language,
            result.RulesVersion,
            result.DefaultActivity,
            result.Activities.Select(activity => new MuhurtaActivityResponse(
                activity.Key, activity.Name, activity.Description, activity.Icon, activity.Order, activity.RequiresInput, activity.Completeness)).ToArray());

    public static GoodTimesResponse ToResponse(this GoodTimesResult result) =>
        new(
            result.Location.ToResponse(),
            new MuhurtaTraditionResponse(result.Tradition.Key, result.Tradition.Name),
            ToResponse(result.Activity),
            result.Date,
            result.DateDisplay,
            result.LocalToday,
            ToResponse(result.DisplayFormat, result.Segments.Count > 0 ? result.Segments[0].StartDisplay : result.DateDisplay),
            result.GeneratedAt,
            result.Language,
            result.RulesVersion,
            result.CalculationVersion,
            ToResponse(result.DayStatus),
            result.Segments.Select(ToResponse).ToArray(),
            result.GoodTimes,
            result.NoGoodTimeReason,
            result.NoGoodTimeMessage,
            result.NowSegmentId,
            result.NextGoodTimeId,
            result.Notices,
            result.Disclaimer);

    public static BestGoodTimesResponse ToResponse(this BestGoodTimesResult result) =>
        new(
            result.Location.ToResponse(),
            new MuhurtaTraditionResponse(result.Tradition.Key, result.Tradition.Name),
            ToResponse(result.Activity),
            result.LocalToday,
            ToResponse(result.DisplayFormat, result.Options.Count > 0 ? result.Options[0].Segment.StartDisplay : string.Empty),
            result.GeneratedAt,
            result.Language,
            result.RulesVersion,
            result.CalculationVersion,
            result.SearchedFrom,
            result.SearchedThrough,
            result.Options.Select(option => new BestGoodTimeResponse(option.Rank, option.Date, option.DateDisplay, ToResponse(option.Segment))).ToArray(),
            result.NoGoodTimeMessage,
            result.Notices,
            result.Disclaimer);

    public static DaySummariesResponse ToResponse(this DaySummariesResult result) =>
        new(
            result.Location.ToResponse(),
            new MuhurtaTraditionResponse(result.Tradition.Key, result.Tradition.Name),
            ToResponse(result.Activity),
            result.LocalToday,
            ToResponse(result.DisplayFormat, result.Days.Count > 0 ? result.Days[0].DateDisplay : string.Empty),
            result.Language,
            result.RulesVersion,
            result.Days.Select(ToResponse).ToArray());

    public static GoodTimeExplanationResponse ToResponse(this GoodTimeExplanationResult result) =>
        new(
            ToResponse(result.Activity),
            result.Date,
            result.Language,
            result.RulesVersion,
            result.Explanation.Source,
            result.Explanation.Summary,
            result.Explanation.Notes.Select(note => new GoodTimeNoteResponse(note.SegmentId, note.Text)).ToArray(),
            result.Explanation.Provider,
            result.Explanation.Model,
            result.Explanation.PromptVersion,
            result.Explanation.FallbackReason,
            result.Disclaimer);

    public static MuhurtaDaySummaryResponse ToResponse(this MuhurtaDaySummary day) =>
        new(
            day.Date,
            day.DateDisplay,
            ToResponse(day.Status),
            day.GoodTimeCount,
            day.BestGoodTime is { } best
                ? new GoodTimeWindowResponse(best.Start, best.End, best.StartDisplay, best.EndDisplay, ToResponse(best.Status))
                : null,
            day.NoGoodTimeReason);

    private static GoodTimeSegmentResponse ToResponse(GoodTimeSegment segment) =>
        new(
            segment.Id,
            segment.Start,
            segment.End,
            segment.StartDisplay,
            segment.EndDisplay,
            segment.TimeRangeDisplay,
            ToResponse(segment.Status),
            segment.Score,
            segment.IsGoodTime,
            segment.Headline,
            segment.Reasons.Select(reason => new GoodTimeReasonResponse(reason.Type, reason.Code, reason.Label, reason.Subject)).ToArray(),
            segment.Conflicts.Select(conflict => new GoodTimeConflictResponse(
                conflict.Code,
                conflict.Kind,
                conflict.Severity,
                conflict.Label,
                conflict.WithFavorable,
                conflict.Start,
                conflict.End,
                conflict.StartDisplay,
                conflict.EndDisplay,
                conflict.OverlapStart,
                conflict.OverlapEnd,
                conflict.OverlapStartDisplay,
                conflict.OverlapEndDisplay)).ToArray());

    private static EvaluatedActivityResponse ToResponse(EvaluatedActivity activity) =>
        new(activity.Key, activity.Name, activity.Icon, activity.Requested, activity.RequestedText, activity.Completeness);

    private static MuhurtaStatusResponse ToResponse(MuhurtaStatusInfo status) => new(status.Status, status.Name, status.Icon);

    private static DisplayFormatResponse ToResponse(DateTimeDisplayFormat format, string example) =>
        new(format.DatePattern, format.TimePattern, format.CountryCode, example);
}
