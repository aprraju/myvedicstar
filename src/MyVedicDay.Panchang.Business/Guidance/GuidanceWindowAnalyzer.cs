using MyVedicDay.Localization;
using MyVedicDay.Panchang.Business.Models;
using MyVedicDay.Panchang.Business.Localization;

namespace MyVedicDay.Panchang.Business.Guidance;

/// <summary>The day's time windows, from the periods the tradition shows.</summary>
internal sealed record GuidanceWindows(
    IReadOnlyList<GuidanceWindow> Best,
    IReadOnlyList<PanchangTimePeriod> Favorable,
    IReadOnlyList<PanchangTimePeriod> Spiritual,
    IReadOnlyList<PanchangTimePeriod> Caution,
    IReadOnlyList<PanchangTimePeriod> Avoid,
    IReadOnlyList<GuidanceMixedWindow> Mixed);

/// <summary>
/// Groups the tradition's Muhurta-style periods by classification and finds the best windows: the longest daytime
/// stretches (sunrise to sunset) not overlapping any caution or avoid period. Periods come from independent rules (e.g.
/// Amrit Kalam from the Nakshatra, Rahu Kalam from the weekday and daylight), so a favorable period can overlap a caution
/// or avoid period; neither cancels the other, and the overlap is reported as a mixed window.
/// </summary>
internal static class GuidanceWindowAnalyzer
{
    public static GuidanceWindows Analyze(
        DailyPanchang day, ResolvedGuidanceRules rules, LocalizedContent content, LocationDateTimeFormat format)
    {
        var periods = day.Periods
            .Where(period => period.Group == ContentKeys.MuhurtaGroup)
            .OrderBy(period => period.Start)
            .ToArray();

        var spiritual = periods.Where(period => rules.SpiritualPeriods.Contains(period.Kind)).ToArray();
        var favorable = periods
            .Where(period => period.Classification is PeriodClassification.HighlyFavorable or PeriodClassification.Favorable)
            .Except(spiritual)
            .ToArray();
        var caution = periods.Where(period => period.Classification == PeriodClassification.Caution).ToArray();
        var avoid = periods.Where(period => period.Classification == PeriodClassification.Avoid).ToArray();
        var unfavorable = caution.Concat(avoid).OrderBy(period => period.Start).ToArray();

        var best = FindBestWindows(day.Sun, unfavorable, rules)
            .Select(window => new GuidanceWindow(window.Start, window.End, format.FormatDateTime(window.Start), format.FormatDateTime(window.End)))
            .ToArray();

        var mixedRating = content.GetEntry(ContentKeys.GuidanceRatings, nameof(GuidanceRating.Mixed));
        var mixed = FindMixedWindows(periods.Where(PeriodOverlapAnnotator.IsFavorable).ToArray(), unfavorable)
            .Select(window => new GuidanceMixedWindow(
                window.Start,
                window.End,
                format.FormatDateTime(window.Start),
                format.FormatDateTime(window.End),
                GuidanceRating.Mixed,
                mixedRating.Name,
                mixedRating.Icon ?? string.Empty,
                window.Favorable,
                window.Unfavorable))
            .ToArray();

        return new GuidanceWindows(best, favorable, spiritual, caution, avoid, mixed);
    }

    /// <summary>
    /// Stretches where at least one favorable period and at least one caution or avoid period are both in force.
    /// Contiguous stretches are merged, and each lists every period involved.
    /// </summary>
    private static IEnumerable<MixedStretch> FindMixedWindows(
        IReadOnlyList<PanchangTimePeriod> favorable, IReadOnlyList<PanchangTimePeriod> unfavorable)
    {
        if (favorable.Count == 0 || unfavorable.Count == 0)
        {
            return [];
        }

        var boundaries = favorable.Concat(unfavorable)
            .SelectMany(period => new[] { period.Start, period.End })
            .Distinct()
            .Order()
            .ToArray();

        var stretches = new List<MixedStretch>();
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var (start, end) = (boundaries[index], boundaries[index + 1]);
            var activeFavorable = favorable.Where(period => period.Start < end && start < period.End).ToArray();
            var activeUnfavorable = unfavorable.Where(period => period.Start < end && start < period.End).ToArray();
            if (activeFavorable.Length == 0 || activeUnfavorable.Length == 0)
            {
                continue;
            }

            if (stretches.Count > 0 && stretches[^1].End == start)
            {
                var previous = stretches[^1];
                stretches[^1] = new MixedStretch(
                    previous.Start, end, previous.Favorable.Union(activeFavorable).ToArray(), previous.Unfavorable.Union(activeUnfavorable).ToArray());
            }
            else
            {
                stretches.Add(new MixedStretch(start, end, activeFavorable, activeUnfavorable));
            }
        }

        return stretches;
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> FindBestWindows(
        SunTimes sun, IReadOnlyList<PanchangTimePeriod> blocked, ResolvedGuidanceRules rules)
    {
        var free = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        var cursor = sun.Sunrise;

        foreach (var period in blocked.Where(period => period.End > sun.Sunrise && period.Start < sun.Sunset).OrderBy(period => period.Start))
        {
            if (period.Start > cursor)
            {
                free.Add((cursor, period.Start));
            }

            if (period.End > cursor)
            {
                cursor = period.End;
            }
        }

        if (cursor < sun.Sunset)
        {
            free.Add((cursor, sun.Sunset));
        }

        var minimum = TimeSpan.FromMinutes(rules.MinimumBestWindowMinutes);
        return free
            .Where(window => window.End - window.Start >= minimum)
            .OrderByDescending(window => window.End - window.Start)
            .Take(rules.MaximumBestWindows)
            .OrderBy(window => window.Start);
    }

    private sealed record MixedStretch(
        DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<PanchangTimePeriod> Favorable, IReadOnlyList<PanchangTimePeriod> Unfavorable);
}
