using Microsoft.Extensions.Options;
using MyVedicDay.Localization;
using MyVedicDay.Location;
using MyVedicDay.Panchang.Business.Configuration;
using MyVedicDay.Panchang.Business.Daily;
using MyVedicDay.Panchang.Business.Models;
using MyVedicDay.Panchang.Data.Content;

namespace MyVedicDay.Panchang.Business.Localization;

/// <summary>
/// Turns language-neutral daily data into what a user of a given tradition and language sees: only the tradition's
/// timings, with regional names, classifications and a matching glossary.
/// </summary>
internal sealed class PanchangLocalizer(
    ILocalizedContentProvider contentProvider,
    IDateTimeDisplayFormatter displayFormatter,
    IOptions<PanchangOptions> panchangOptions)
{
    /// <summary>Content in the negotiated language, with the tradition's regional names taking precedence.</summary>
    public Task<LocalizedContent> GetContentAsync(
        IReadOnlyList<string>? preferredLanguages, TraditionProfile tradition, CancellationToken cancellationToken) =>
        contentProvider.GetContentAsync(preferredLanguages, tradition.Key, cancellationToken);

    /// <summary>Content in the negotiated language without tradition-specific names.</summary>
    public Task<LocalizedContent> GetContentAsync(IReadOnlyList<string>? preferredLanguages, CancellationToken cancellationToken) =>
        contentProvider.GetContentAsync(preferredLanguages, tradition: null, cancellationToken);

    /// <summary>Display format for the location: its country's date convention and its time zone abbreviations.</summary>
    public LocationDateTimeFormat CreateFormat(ResolvedLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return displayFormatter.CreateFormat(location.TimeZone, location.Place?.CountryCode);
    }

    public PanchangTradition DescribeTradition(TraditionProfile profile, LocalizedContent content)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(content);

        return new PanchangTradition(
            profile.Key,
            content.GetName(ContentKeys.Traditions, profile.Key),
            content.GetEntry(ContentKeys.PanchangTraditions, profile.Key).Description,
            profile.Display);
    }

    /// <summary>
    /// Glossary for the tradition: every element, term (Choghadiya only when shown), classification and the periods
    /// the tradition displays.
    /// </summary>
    public GlossaryResult BuildGlossary(LocalizedContent content, TraditionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(profile);

        var showsChoghadiya = ShowsChoghadiya(profile);
        var periodIds = content.GetIds(ContentKeys.Periods)
            .Where(kind => IsShown(profile, kind, IsChoghadiyaKind(kind) ? ContentKeys.ChoghadiyaGroup : null));
        var termIds = content.GetIds(ContentKeys.Terms)
            .Where(term => showsChoghadiya || term != ContentKeys.ChoghadiyaTerm);

        var sections = new Dictionary<string, IReadOnlyDictionary<string, LocalizedEntry>>(StringComparer.Ordinal)
        {
            [ContentKeys.Periods] = ToSection(content, ContentKeys.Periods, periodIds),
            [ContentKeys.Elements] = ToSection(content, ContentKeys.Elements, content.GetIds(ContentKeys.Elements)),
            [ContentKeys.Terms] = ToSection(content, ContentKeys.Terms, termIds),
            [ContentKeys.Classifications] = ToSection(content, ContentKeys.Classifications, content.GetIds(ContentKeys.Classifications)),
            [ContentKeys.GuidanceRatings] = ToSection(content, ContentKeys.GuidanceRatings, content.GetIds(ContentKeys.GuidanceRatings)),
        };

        return new GlossaryResult(content.Language, sections);
    }

    public DailyPanchang Localize(
        DailyPanchangData day, LocalizedContent content, TraditionProfile profile, LocationDateTimeFormat format)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(format);

        return new DailyPanchang(
            day.Date,
            format.FormatDate(day.Date),
            day.Weekday,
            content.GetName(ContentKeys.Weekday, day.Weekday.ToString()),
            day.Sun,
            new SunTimesDisplay(
                format.FormatDateTime(day.Sun.Sunrise), format.FormatDateTime(day.Sun.Sunset), format.FormatDateTime(day.Sun.NextSunrise)),
            Localize(day.Tithis, ContentKeys.Tithi, content, format),
            Localize(day.Nakshatras, ContentKeys.Nakshatra, content, format),
            Localize(day.Yogas, ContentKeys.Yoga, content, format),
            Localize(day.Karanas, ContentKeys.Karana, content, format),
            day.Periods
                .Where(period => IsShown(profile, period.Kind, period.Group))
                .Select(period => new PanchangTimePeriod(
                    period.Kind,
                    period.Group,
                    content.GetName(ContentKeys.Periods, period.Kind),
                    panchangOptions.Value.PeriodClassifications[period.Kind],
                    period.IsAuspicious,
                    period.Start,
                    period.End,
                    format.FormatDateTime(period.Start),
                    format.FormatDateTime(period.End)))
                .ToArray());
    }

    private static bool ShowsChoghadiya(TraditionProfile profile) =>
        profile.Display.Contains(ContentKeys.ChoghadiyaGroup, StringComparer.OrdinalIgnoreCase);

    /// <summary>A period is shown when its kind, or its group (for Choghadiya), is in the tradition's display list.</summary>
    private static bool IsShown(TraditionProfile profile, string kind, string? group) =>
        group?.Equals(ContentKeys.ChoghadiyaGroup, StringComparison.OrdinalIgnoreCase) == true
            ? ShowsChoghadiya(profile)
            : profile.Display.Contains(kind, StringComparer.OrdinalIgnoreCase);

    private static bool IsChoghadiyaKind(string kind) => kind.StartsWith(ContentKeys.ChoghadiyaGroup, StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, LocalizedEntry> ToSection(LocalizedContent content, string category, IEnumerable<string> ids) =>
        ids.ToDictionary(id => id, id => content.GetEntry(category, id), StringComparer.Ordinal);

    private static PanchangElementPeriod[] Localize(
        IReadOnlyList<ElementPeriodData> periods, string category, LocalizedContent content, LocationDateTimeFormat format) =>
        periods.Select(period => new PanchangElementPeriod(
            period.Key,
            period.Number,
            content.GetName(category, period.Key),
            period.Start,
            period.End,
            format.FormatDateTime(period.Start),
            format.FormatDateTime(period.End))).ToArray();
}
