using System.Globalization;
using Microsoft.Extensions.Options;
using MyVedicDay.Astronomy;
using MyVedicDay.Localization;
using MyVedicDay.Location;
using MyVedicDay.Muhurta.Business.Activities;
using MyVedicDay.Muhurta.Business.Daily;
using MyVedicDay.Muhurta.Business.Explanation;
using MyVedicDay.Muhurta.Business.Rules;
using MyVedicDay.Muhurta.Calculation;
using MyVedicDay.Muhurta.Data;
using MyVedicDay.Panchang.Calculation.Models;
using MyVedicDay.Shared.Exceptions;
using MyVedicDay.Traditions;
using NodaTime;

namespace MyVedicDay.Muhurta.Business;

/// <summary>
/// Orchestrates Find a Good Time: resolve the tradition and location (shared modules) and the activity, retrieve the
/// cached evaluation, then localize it. Statuses, times and Good Times are passed through unchanged from the engine.
/// </summary>
internal sealed class MuhurtaFacade(
    ITraditionRegistry traditions,
    ILocationResolver locationResolver,
    MuhurtaRulesProvider rules,
    ActivityResolver activities,
    MuhurtaDayService dayService,
    ILocalizedContentProvider contentProvider,
    IDateTimeDisplayFormatter displayFormatter,
    GoodTimeExplainer explainer,
    IOptions<MuhurtaOptions> options,
    IClock clock) : IMuhurtaFacade
{

    public async Task<MuhurtaNakshatrasResult> GetNakshatrasAsync(
        string? tradition, IReadOnlyList<string>? preferredLanguages, CancellationToken cancellationToken)
    {
        var traditionKey = await traditions.ResolveAsync(tradition, cancellationToken);
        var content = await contentProvider.GetContentAsync(preferredLanguages, traditionKey, cancellationToken);
        return new(content.Language, Enum.GetValues<Nakshatra>()
            .Select(star => new MuhurtaNakshatra(star.ToString(), content.GetName("nakshatra", star.ToString()), (int)star)).ToArray());
    }

    public async Task<MuhurtaActivitiesResult> GetActivitiesAsync(
        string? tradition, IReadOnlyList<string>? preferredLanguages, CancellationToken cancellationToken)
    {
        var traditionKey = await traditions.ResolveAsync(tradition, cancellationToken);
        var document = await rules.GetDocumentAsync(cancellationToken);
        var content = await contentProvider.GetContentAsync(preferredLanguages, traditionKey, cancellationToken);

        return new MuhurtaActivitiesResult(
            content.Language,
            document.Version,
            options.Value.DefaultActivity,
            document.Activities.Select((activity, index) =>
            {
                var entry = content.GetEntry(MuhurtaContent.Activities, activity.Key);
                return new MuhurtaActivity(
                    activity.Key, entry.Name, entry.Description, activity.Icon, index + 1, activity.RequiresInput, Completeness(activity));
            }).ToArray());
    }

    public async Task<GoodTimesResult> GetGoodTimesAsync(MuhurtaQuery query, GoodTimesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);

        var (context, activity, content, format, localToday, now) = await PrepareAsync(query, request.Activity, request.OtherText, cancellationToken);
        var date = request.Date is { } requested ? LocalDate.FromDateOnly(requested) : localToday;
        EnsureSupported(date, date);

        var day = await dayService.GetDayAsync(context, date, cancellationToken);
        var segments = day.Segments.Select((segment, index) => Localize(segment, index, activity, content, format, date.ToDateOnly())).ToArray();

        string? nowId = null;
        string? nextId = null;
        if (date == localToday)
        {
            var instant = now.ToDateTimeOffset();
            nowId = segments.FirstOrDefault(segment => segment.Start <= instant && instant < segment.End)?.Id;
            nextId = segments.FirstOrDefault(segment => segment.IsGoodTime && segment.Start > instant)?.Id;
        }

        return new GoodTimesResult(
            context.Location.ToDetails(),
            new MuhurtaTradition(context.Tradition, content.GetName(MuhurtaContent.Traditions, context.Tradition)),
            activity,
            date.ToDateOnly(),
            format.FormatDate(date.ToDateOnly()),
            localToday.ToDateOnly(),
            format.Format,
            now.ToDateTimeOffset(),
            content.Language,
            context.RulesVersion,
            MuhurtaCalculationVersion.Current,
            Status(day.DayStatus, content),
            segments,
            day.GoodTimes.Select(index => segments[index].Id).ToArray(),
            day.NoGoodTimeReason,
            day.NoGoodTimeReason is null ? null : content.GetEntry(MuhurtaContent.NoGoodTime, day.NoGoodTimeReason).Message,
            nowId,
            nextId,
            Notices(activity, content),
            Text(content, "disclaimer"));
    }

    public async Task<GoodTimeExplanationResult> GetExplanationAsync(
        MuhurtaQuery query, GoodTimesRequest request, CancellationToken cancellationToken)
    {
        var result = await GetGoodTimesAsync(query, request, cancellationToken);
        var content = await contentProvider.GetContentAsync(query.PreferredLanguages, result.Tradition.Key, cancellationToken);
        var explanation = await explainer.ExplainAsync(result, content, cancellationToken);

        return new GoodTimeExplanationResult(result.Activity, result.Date, result.Language, result.RulesVersion, explanation, result.Disclaimer);
    }

    public async Task<DaySummariesResult> GetDaySummariesAsync(
        MuhurtaQuery query, DateOnly startDate, int days, string? activity, string? otherText, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (days < 1 || days > options.Value.MaxRangeDays)
        {
            throw new ValidationException("days", $"Days must be between 1 and {options.Value.MaxRangeDays}.");
        }

        var start = LocalDate.FromDateOnly(startDate);
        EnsureSupported(start, start.PlusDays(days - 1));

        var (context, evaluated, content, format, localToday, _) = await PrepareAsync(query, activity, otherText, cancellationToken);

        // Fetch all days concurrently — cache reads happen in parallel and misses are coalesced per key.
        var dayTasks = Enumerable.Range(0, days)
            .Select(i => dayService.GetDayAsync(context, start.PlusDays(i), cancellationToken))
            .ToArray();
        var dayDatas = await Task.WhenAll(dayTasks);

        var summaries = new MuhurtaDaySummary[days];
        for (var i = 0; i < days; i++)
        {
            var date = start.PlusDays(i);
            var day  = dayDatas[i];
            var best = day.GoodTimes.Count > 0 ? day.Segments[day.GoodTimes[0]] : null;
            summaries[i] = new MuhurtaDaySummary(
                date.ToDateOnly(),
                format.FormatDate(date.ToDateOnly()),
                Status(day.DayStatus, content),
                day.GoodTimes.Count,
                best is null
                    ? null
                    : new GoodTimeWindow(best.Start, best.End, format.FormatDateTime(best.Start), format.FormatDateTime(best.End), Status(best.Status, content)),
                day.NoGoodTimeReason);
        }

        return new DaySummariesResult(
            context.Location.ToDetails(),
            new MuhurtaTradition(context.Tradition, content.GetName(MuhurtaContent.Traditions, context.Tradition)),
            evaluated,
            localToday.ToDateOnly(),
            format.Format,
            content.Language,
            context.RulesVersion,
            summaries);
    }

    public async Task<BestGoodTimesResult> GetBestGoodTimesAsync(
        MuhurtaQuery query, BestGoodTimesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);

        var settings = options.Value;
        var count = request.Count ?? settings.BestDefaultCount;
        if (count < 1 || count > settings.BestMaxCount)
        {
            throw new ValidationException("count", $"Count must be between 1 and {settings.BestMaxCount}.");
        }

        var days = request.Days ?? settings.BestSearchDefaultDays;
        if (days < 1 || days > settings.BestSearchMaxDays)
        {
            throw new ValidationException("days", $"Days must be between 1 and {settings.BestSearchMaxDays}.");
        }

        var (context, activity, content, format, localToday, now) = await PrepareAsync(query, request.Activity, request.OtherText, cancellationToken);
        var last = localToday.PlusDays(days - 1);
        EnsureSupported(localToday, last);

        // Prefetch all days concurrently so cache reads (usually the bulk of the work) happen in parallel.
        // The selector still processes days in order and exits early once it has enough results.
        var bestDayTasks = Enumerable.Range(0, days)
            .Select(i => dayService.GetDayAsync(context, localToday.PlusDays(i), cancellationToken))
            .ToArray();

        var selector = new BestGoodTimeSelector(count, now.ToDateTimeOffset());
        for (var i = 0; i < days && !selector.IsComplete; i++)
        {
            selector.Consider(localToday.PlusDays(i), await bestDayTasks[i]);
        }

        var choices = selector.Results
            .Select((choice, index) =>
            {
                var date = choice.Date.ToDateOnly();
                var segment = Localize(choice.Day.Segments[choice.SegmentIndex], choice.SegmentIndex, activity, content, format, date);
                return new BestGoodTime(index + 1, date, format.FormatDate(date), segment);
            })
            .ToArray();

        return new BestGoodTimesResult(
            context.Location.ToDetails(),
            new MuhurtaTradition(context.Tradition, content.GetName(MuhurtaContent.Traditions, context.Tradition)),
            activity,
            localToday.ToDateOnly(),
            format.Format,
            now.ToDateTimeOffset(),
            content.Language,
            context.RulesVersion,
            MuhurtaCalculationVersion.Current,
            localToday.ToDateOnly(),
            last.ToDateOnly(),
            choices,
            choices.Length > 0
                ? null
                : Text(content, "noBestGoodTime").Replace("{days}", days.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
            Notices(activity, content),
            Text(content, "disclaimer"));
    }

    private async Task<(MuhurtaRequestContext Context, EvaluatedActivity Activity, LocalizedContent Content, LocationDateTimeFormat Format, LocalDate LocalToday, Instant Now)>
        PrepareAsync(MuhurtaQuery query, string? activity, string? otherText, CancellationToken cancellationToken)
    {
        var birthNakshatra = BirthNakshatra.Parse(query.Nakshatra);
        // Phase 1: tradition, location, activity resolution and document are independent — run in parallel.
        // Activity resolution may call AI for "other" text, so parallelising it with the fast calls is important.
        var traditionTask = traditions.ResolveAsync(query.Tradition, cancellationToken);
        var activityTask  = activities.ResolveAsync(activity, otherText, cancellationToken);
        var locationTask  = locationResolver.ResolveAsync(query.Location, cancellationToken);
        var documentTask  = rules.GetDocumentAsync(cancellationToken);
        await Task.WhenAll(traditionTask, activityTask, locationTask, documentTask);
        var tradition = traditionTask.Result;
        var resolved  = activityTask.Result;
        var location  = locationTask.Result;
        var document  = documentTask.Result;

        // Phase 2: profile (needs activity + tradition) and content (needs tradition) — run in parallel.
        var profileTask = rules.GetProfileAsync(resolved.Definition.Key, tradition, cancellationToken);
        var contentTask = contentProvider.GetContentAsync(query.PreferredLanguages, tradition, cancellationToken);
        await Task.WhenAll(profileTask, contentTask);
        var profile = profileTask.Result;
        var content = contentTask.Result;

        var now = clock.GetCurrentInstant();
        var entry = content.GetEntry(MuhurtaContent.Activities, resolved.Definition.Key);
        var evaluated = new EvaluatedActivity(
            resolved.Definition.Key, entry.Name, resolved.Definition.Icon, resolved.Requested, resolved.RequestedText, Completeness(resolved.Definition));

        return (
            new MuhurtaRequestContext(location, tradition, profile, document.Version, birthNakshatra),
            evaluated,
            content,
            displayFormatter.CreateFormat(location.TimeZone, location.Place?.CountryCode),
            now.InZone(location.TimeZone).Date,
            now);
    }

    private static GoodTimeSegment Localize(
        MuhurtaSegmentData segment, int index, EvaluatedActivity activity, LocalizedContent content, LocationDateTimeFormat format, DateOnly date)
    {
        var status = Status(segment.Status, content);
        return new GoodTimeSegment(
            $"s{index + 1}",
            segment.Start,
            segment.End,
            format.FormatDateTime(segment.Start),
            format.FormatDateTime(segment.End),
            format.FormatTimeRange(segment.Start, segment.End, date, content.GetEntry("guidanceText", "nextDay").Message ?? "(+1)"),
            status,
            segment.Score,
            segment.IsGoodTime,
            Text(content, "headline").Replace("{status}", status.Name, StringComparison.Ordinal).Replace("{activity}", activity.Name, StringComparison.Ordinal),
            segment.Reasons.Select(reason => new GoodTimeReason(
                reason.Type, reason.Code, content.GetEntry(MuhurtaContent.Reasons, reason.Code).Name, reason.Subject)).ToArray(),
            segment.Conflicts.Select(conflict => new GoodTimeConflict(
                conflict.Code,
                conflict.Kind,
                conflict.Severity,
                content.GetEntry(MuhurtaContent.Conflicts, conflict.Code).Name,
                conflict.WithFavorable,
                conflict.PeriodStart,
                conflict.PeriodEnd,
                format.FormatDateTime(conflict.PeriodStart),
                format.FormatDateTime(conflict.PeriodEnd),
                conflict.OverlapStart,
                conflict.OverlapEnd,
                format.FormatDateTime(conflict.OverlapStart),
                format.FormatDateTime(conflict.OverlapEnd))).ToArray());
    }

    private static IReadOnlyList<string> Notices(EvaluatedActivity activity, LocalizedContent content)
    {
        var notices = new List<string>();
        if (activity.Requested != activity.Key)
        {
            notices.Add(Text(content, "evaluatedAs").Replace("{activity}", activity.Name, StringComparison.Ordinal));
        }

        if (activity.Completeness == MuhurtaContent.Partial)
        {
            notices.Add(Text(content, "partialRules"));
        }

        return notices;
    }

    private static MuhurtaStatusInfo Status(string status, LocalizedContent content)
    {
        var entry = content.GetEntry(MuhurtaContent.Statuses, status);
        return new MuhurtaStatusInfo(status, entry.Name, entry.Icon);
    }

    private static string Text(LocalizedContent content, string id) => content.GetEntry(MuhurtaContent.Text, id).Message ?? string.Empty;

    private static string Completeness(MuhurtaActivityDefinition activity) => activity.Completeness ?? MuhurtaContent.Full;

    private static void EnsureSupported(LocalDate first, LocalDate last)
    {
        if (first.Year < SupportedDates.MinYear || last.Year > SupportedDates.MaxYear)
        {
            throw new ValidationException(
                "date", $"Dates must fall between {SupportedDates.MinYear}-01-01 and {SupportedDates.MaxYear}-12-31.");
        }
    }
}

/// <summary>Content categories and values owned by the Muhurta module.</summary>
internal static class MuhurtaContent
{
    public const string Activities = "muhurtaActivities";
    public const string Statuses = "muhurtaStatuses";
    public const string Reasons = "muhurtaReasons";
    public const string Conflicts = "muhurtaConflicts";
    public const string Text = "muhurtaText";
    public const string NoGoodTime = "muhurtaNoGoodTime";
    public const string Traditions = "traditions";
    public const string Full = "full";
    public const string Partial = "partial";
}
