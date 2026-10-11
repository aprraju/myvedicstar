using MyVedicDay.Localization;
using MyVedicDay.Panchang.Business.Localization;
using MyVedicDay.Panchang.Business.Models;

namespace MyVedicDay.Panchang.Business.Guidance;

/// <summary>
/// Marks each favorable Muhurta-style period with the caution and avoid periods that overlap it, and a localized note
/// naming the times to avoid. Periods come from independent rules (e.g. Amrit Kalam from the Nakshatra, Rahu Kalam from
/// the weekday and daylight), so neither cancels the other: the favorable period keeps its full times.
/// </summary>
internal static class PeriodOverlapAnnotator
{
    public static bool IsFavorable(PanchangTimePeriod period) =>
        period.Group == ContentKeys.MuhurtaGroup
        && period.Classification is PeriodClassification.HighlyFavorable or PeriodClassification.Favorable;

    public static bool IsUnfavorable(PanchangTimePeriod period) =>
        period.Group == ContentKeys.MuhurtaGroup
        && period.Classification is PeriodClassification.Caution or PeriodClassification.Avoid;

    public static DailyPanchang Annotate(DailyPanchang day, LocalizedContent content, LocationDateTimeFormat format)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(format);

        var unfavorable = day.Periods.Where(IsUnfavorable).OrderBy(period => period.Start).ToArray();
        if (unfavorable.Length == 0)
        {
            return day;
        }

        var nextDay = content.GetEntry(ContentKeys.GuidanceText, "nextDay").Message ?? string.Empty;
        return day with
        {
            Periods = day.Periods
                .Select(period => IsFavorable(period) ? Annotate(period, unfavorable, day.Date, content, format, nextDay) : period)
                .ToArray(),
        };
    }

    private static PanchangTimePeriod Annotate(
        PanchangTimePeriod period,
        IReadOnlyList<PanchangTimePeriod> unfavorable,
        DateOnly date,
        LocalizedContent content,
        LocationDateTimeFormat format,
        string nextDay)
    {
        var overlapping = unfavorable.Where(other => other.Start < period.End && period.Start < other.End).ToArray();
        if (overlapping.Length == 0)
        {
            return period;
        }

        var overlaps = overlapping
            .Select(other =>
            {
                var start = other.Start > period.Start ? other.Start : period.Start;
                var end = other.End < period.End ? other.End : period.End;
                return new PeriodOverlap(
                    other.Kind, other.Name, other.Classification, start, end, format.FormatDateTime(start), format.FormatDateTime(end));
            })
            .ToArray();

        var message = string.Join(' ', overlapping.Zip(overlaps, (other, overlap) =>
            (content.GetEntry(ContentKeys.PeriodOverlap, other.Classification.ToString()).Message ?? string.Empty)
                .Replace("{name}", other.Name, StringComparison.Ordinal)
                .Replace("{period}", format.FormatTimeRange(other.Start, other.End, date, nextDay), StringComparison.Ordinal)
                .Replace("{range}", format.FormatTimeRange(overlap.Start, overlap.End, date, nextDay), StringComparison.Ordinal)));

        return period with { Overlaps = overlaps, OverlapMessage = message };
    }
}
