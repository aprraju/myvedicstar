using Microsoft.Extensions.Options;
using MyVedicDay.Astronomy;
using MyVedicDay.Festivals.Business.Daily;
using MyVedicDay.Festivals.Business.Rules;
using MyVedicDay.Festivals.Calculation;
using MyVedicDay.Localization;
using MyVedicDay.Location;
using MyVedicDay.Shared.Exceptions;
using MyVedicDay.Traditions;
using NodaTime;

namespace MyVedicDay.Festivals.Business;

/// <summary>
/// Orchestrates a festival request: resolve the tradition (shared Traditions module) and location (shared Location
/// module), determine the location's local dates, retrieve the tradition-neutral daily festivals through the cache, then
/// keep the tradition's festivals and localize them with its regional names.
/// </summary>
internal sealed class FestivalFacade(
    ITraditionRegistry traditions,
    ILocationResolver locationResolver,
    FestivalRulesProvider rulesProvider,
    FestivalDayService festivalDays,
    ILocalizedContentProvider contentProvider,
    IDateTimeDisplayFormatter displayFormatter,
    IOptions<FestivalOptions> options,
    IClock clock) : IFestivalFacade
{
    /// <summary>Range supported by the ephemeris and ΔT model with verified accuracy (as for the Panchang).</summary>

    private const string CategoriesCategory = "festivalCategories";
    private const string FestivalsCategory = "festivals";
    private const string ObservanceTimesCategory = "observanceTimes";
    private const string TraditionsCategory = "traditions";

    public Task<FestivalsResult> GetUpcomingAsync(FestivalQuery query, int days, CancellationToken cancellationToken)
    {
        ValidateDays(days, options.Value.MaxUpcomingDays);
        return GetAsync(query, today => today, days, cancellationToken);
    }

    public Task<FestivalsResult> GetRangeAsync(FestivalQuery query, DateOnly startDate, int days, CancellationToken cancellationToken)
    {
        ValidateDays(days, options.Value.MaxRangeDays);
        return GetAsync(query, _ => LocalDate.FromDateOnly(startDate), days, cancellationToken);
    }

    private async Task<FestivalsResult> GetAsync(
        FestivalQuery query, Func<LocalDate, LocalDate> selectStart, int days, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Phase 1: tradition, location and rules are fully independent — run in parallel.
        var traditionTask = traditions.ResolveAsync(query.Tradition, cancellationToken);
        var locationTask  = locationResolver.ResolveAsync(query.Location, cancellationToken);
        var rulesTask     = rulesProvider.GetRulesAsync(cancellationToken);
        await Task.WhenAll(traditionTask, locationTask, rulesTask);
        var tradition = traditionTask.Result;
        var location  = locationTask.Result;
        var rules     = rulesTask.Result;

        var now = clock.GetCurrentInstant();
        var localToday = now.InZone(location.TimeZone).Date;
        var dates = BuildDates(selectStart(localToday), days);

        // Phase 2: festival data (needs location + rules + dates) and content (needs tradition) — run in parallel.
        var festivalDataTask = festivalDays.GetDaysAsync(location, rules, dates, cancellationToken);
        var contentTask      = contentProvider.GetContentAsync(query.PreferredLanguages, tradition, cancellationToken);
        await Task.WhenAll(festivalDataTask, contentTask);
        var festivalData = festivalDataTask.Result;
        var content      = contentTask.Result;
        var format = displayFormatter.CreateFormat(location.TimeZone, location.Place?.CountryCode);

        var festivals = festivalData
            .SelectMany(day => SelectForTradition(day, tradition, rules)
                .Select(occurrence => Localize(day.Date, occurrence, rules.Metadata[occurrence.Key], localToday, content, format)))
            .ToArray();

        return new FestivalsResult(
            location.ToDetails(),
            new FestivalTradition(tradition, content.GetName(TraditionsCategory, tradition)),
            localToday.ToDateOnly(),
            format.FormatDate(localToday.ToDateOnly()),
            dates[0].ToDateOnly(),
            dates[^1].ToDateOnly(),
            format.Format,
            now.ToDateTimeOffset(),
            content.Language,
            rules.Version,
            FestivalCalculationVersion.Current,
            festivals);
    }

    /// <summary>
    /// The tradition's festivals on one date, in rules order, without those superseded by another festival shown on the
    /// same date (e.g. Vaikuntha Ekadashi hides the generic Ekadashi).
    /// </summary>
    private static IEnumerable<FestivalOccurrenceData> SelectForTradition(FestivalDayData day, string tradition, FestivalRules rules)
    {
        var observed = day.Festivals
            .Where(occurrence => rules.Metadata.TryGetValue(occurrence.Key, out var metadata) && metadata.IsObservedBy(tradition))
            .ToArray();
        var superseded = observed.SelectMany(occurrence => rules.Metadata[occurrence.Key].Supersedes).ToHashSet(StringComparer.Ordinal);

        return observed
            .Where(occurrence => !superseded.Contains(occurrence.Key))
            .OrderBy(occurrence => rules.Metadata[occurrence.Key].Order);
    }

    private static Festival Localize(
        DateOnly date,
        FestivalOccurrenceData occurrence,
        FestivalMetadata metadata,
        LocalDate localToday,
        LocalizedContent content,
        LocationDateTimeFormat format)
    {
        var entry = content.GetEntry(FestivalsCategory, occurrence.Key);
        var category = content.GetEntry(CategoriesCategory, metadata.Category);

        return new Festival(
            occurrence.Key,
            entry.Name,
            entry.Description,
            metadata.Category,
            category.Name,
            category.Icon,
            date,
            format.FormatDate(date),
            Period.Between(localToday, LocalDate.FromDateOnly(date), PeriodUnits.Days).Days,
            new FestivalTiming(
                occurrence.Basis,
                occurrence.BasisKey,
                occurrence.BasisStart,
                occurrence.BasisEnd,
                format.FormatDateTime(occurrence.BasisStart),
                format.FormatDateTime(occurrence.BasisEnd),
                occurrence.Time,
                content.GetName(ObservanceTimesCategory, occurrence.Time),
                occurrence.WindowStart,
                occurrence.WindowEnd,
                occurrence.WindowStart is { } start ? format.FormatDateTime(start) : null,
                occurrence.WindowEnd is { } end ? format.FormatDateTime(end) : null));
    }

    private static void ValidateDays(int days, int maxDays)
    {
        if (days < 1 || days > maxDays)
        {
            throw new ValidationException("days", $"Days must be between 1 and {maxDays}.");
        }
    }

    private static LocalDate[] BuildDates(LocalDate start, int days)
    {
        var dates = Enumerable.Range(0, days).Select(start.PlusDays).ToArray();
        if (dates[0].Year < SupportedDates.MinYear || dates[^1].Year > SupportedDates.MaxYear)
        {
            throw new ValidationException(
                "date", $"Dates must fall between {SupportedDates.MinYear}-01-01 and {SupportedDates.MaxYear}-12-31.");
        }

        return dates;
    }
}
