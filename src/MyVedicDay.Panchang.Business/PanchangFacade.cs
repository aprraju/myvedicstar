using MyVedicDay.Localization;
using MyVedicDay.Location;
using MyVedicDay.Panchang.Business.Calendar;
using MyVedicDay.Panchang.Business.Daily;
using MyVedicDay.Panchang.Business.Guidance;
using MyVedicDay.Panchang.Business.Localization;
using MyVedicDay.Panchang.Business.Models;
using MyVedicDay.Panchang.Business.Traditions;
using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Data.Content;
using NodaTime;

namespace MyVedicDay.Panchang.Business;

/// <summary>
/// Orchestrates a Panchang request: resolve location (shared Location module) and tradition, determine the location's
/// local dates, retrieve the language-neutral daily entries through the cache, then localize and format them
/// (shared Localization module).
/// </summary>
internal sealed class PanchangFacade(
    ILocationResolver locationResolver,
    PanchangTraditionResolver traditionResolver,
    CalendarRangeBuilder calendar,
    DailyPanchangService dailyPanchang,
    PanchangLocalizer localizer,
    IDailyGuidanceEvaluator guidance,
    IClock clock) : IPanchangFacade
{
    public Task<PanchangResult> GetTodayAsync(PanchangQuery query, CancellationToken cancellationToken) =>
        GetAsync(query, today => calendar.Single(today), cancellationToken);

    public Task<PanchangResult> GetTomorrowAsync(PanchangQuery query, CancellationToken cancellationToken) =>
        GetAsync(query, today => calendar.Single(today.PlusDays(1)), cancellationToken);

    public Task<PanchangResult> GetDateAsync(PanchangQuery query, DateOnly date, CancellationToken cancellationToken) =>
        GetAsync(query, _ => calendar.Single(LocalDate.FromDateOnly(date)), cancellationToken);

    public Task<PanchangResult> GetDateRangeAsync(PanchangQuery query, DateOnly startDate, int days, CancellationToken cancellationToken) =>
        GetAsync(query, _ => calendar.Range(LocalDate.FromDateOnly(startDate), days), cancellationToken);

    public Task<PanchangResult> GetTenDaysAsync(PanchangQuery query, CancellationToken cancellationToken) =>
        GetAsync(query, today => calendar.Range(today, 10), cancellationToken);

    public Task<PanchangResult> GetMonthAsync(PanchangQuery query, int year, int month, CancellationToken cancellationToken) =>
        GetAsync(query, _ => calendar.Month(year, month), cancellationToken);

    public Task<PanchangResult> GetYearAsync(PanchangQuery query, int year, CancellationToken cancellationToken) =>
        GetAsync(query, _ => calendar.Year(year), cancellationToken);

    private async Task<PanchangResult> GetAsync(
        PanchangQuery query, Func<LocalDate, IReadOnlyList<LocalDate>> selectDates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Tradition and location resolution are independent — run them in parallel.
        var traditionTask = traditionResolver.ResolveAsync(query.Tradition, cancellationToken);
        var locationTask  = locationResolver.ResolveAsync(query.Location, cancellationToken);
        await Task.WhenAll(traditionTask, locationTask);
        var tradition = traditionTask.Result;
        var location  = locationTask.Result;

        var now = clock.GetCurrentInstant();
        var localToday = now.InZone(location.TimeZone).Date;
        var dates = selectDates(localToday);

        // Calculation depends only on location and calculation method; the tradition only shapes the presentation.
        var context = new PanchangRequestContext(location, tradition.CalculationMethod);
        var days = await dailyPanchang.GetDaysAsync(context, dates, cancellationToken);
        var content = await localizer.GetContentAsync(query.PreferredLanguages, tradition, cancellationToken);
        var format = localizer.CreateFormat(location);

        return new PanchangResult(
            location.ToDetails(),
            localizer.DescribeTradition(tradition, content),
            tradition.CalculationMethod,
            PanchangCalculationVersion.Current,
            localToday.ToDateOnly(),
            format.FormatDate(localToday.ToDateOnly()),
            format.Format,
            now.ToDateTimeOffset(),
            localizer.BuildGlossary(content, tradition),
            await LocalizeWithGuidanceAsync(days, content, tradition, format, cancellationToken));
    }

    private async Task<IReadOnlyList<DailyPanchang>> LocalizeWithGuidanceAsync(
        IReadOnlyList<DailyPanchangData> days,
        LocalizedContent content,
        TraditionProfile tradition,
        LocationDateTimeFormat format,
        CancellationToken cancellationToken)
    {
        var result = new DailyPanchang[days.Count];
        for (var i = 0; i < days.Count; i++)
        {
            var day = PeriodOverlapAnnotator.Annotate(localizer.Localize(days[i], content, tradition, format), content, format);
            result[i] = day with { Guidance = await guidance.EvaluateAsync(day, tradition.Key, content, format, cancellationToken) };
        }

        return result;
    }
}
