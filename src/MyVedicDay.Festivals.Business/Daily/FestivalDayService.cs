using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVedicDay.Astronomy;
using MyVedicDay.Caching;
using MyVedicDay.Festivals.Business.Rules;
using MyVedicDay.Festivals.Calculation;
using MyVedicDay.Festivals.Calculation.Models;
using MyVedicDay.Location;
using MyVedicDay.Shared.Exceptions;
using NodaTime;
using NodaTime.Text;

namespace MyVedicDay.Festivals.Business.Daily;

/// <summary>
/// Cache-aside retrieval of daily festival entries. Cached days are reused; the missing days are calculated together
/// (a festival's date depends on its neighbouring days), and concurrent identical misses are coalesced.
/// </summary>
internal sealed partial class FestivalDayService(
    IFestivalCalculator calculator,
    IDistributedObjectCache cache,
    FestivalCacheKeyFactory cacheKeys,
    InFlightRequestCoalescer<IReadOnlyList<FestivalDayData>> coalescer,
    IClock clock,
    IOptions<FestivalOptions> options,
    ILogger<FestivalDayService> logger)
{
    public async Task<IReadOnlyList<FestivalDayData>> GetDaysAsync(
        ResolvedLocation location, FestivalRules rules, IReadOnlyList<LocalDate> dates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(dates);

        var keys = dates.Select(date => cacheKeys.Create(location, rules.Version, date)).ToArray();
        var cached = await Task.WhenAll(keys.Select(key => cache.GetAsync<FestivalDayData>(key, cancellationToken)));

        var results = new FestivalDayData?[dates.Count];
        var missing = new List<int>();
        for (var i = 0; i < dates.Count; i++)
        {
            // Guard against a mismatched entry ever being served for another date.
            if (cached[i] is { } hit && hit.Date == dates[i].ToDateOnly())
            {
                results[i] = hit;
            }
            else
            {
                missing.Add(i);
            }
        }

        if (missing.Count > 0)
        {
            var first = dates[missing[0]];
            var last = dates[missing[^1]];
            var rangeKey = $"{keys[missing[0]]}..{LocalDatePattern.Iso.Format(last)}";
            var calculated = await coalescer
                .GetOrStartAsync(rangeKey, () => CalculateAndCacheAsync(location, rules, first, last))
                .WaitAsync(cancellationToken);

            var byDate = calculated.ToDictionary(day => day.Date);
            foreach (var index in missing)
            {
                results[index] = byDate[dates[index].ToDateOnly()];
            }
        }

        LogDaysResolved(dates.Count, dates.Count - missing.Count, missing.Count, location.TimeZone.Id);
        return results.Select(day => day!).ToArray();
    }

    private async Task<IReadOnlyList<FestivalDayData>> CalculateAndCacheAsync(
        ResolvedLocation location, FestivalRules rules, LocalDate first, LocalDate last)
    {
        var started = Stopwatch.GetTimestamp();
        IReadOnlyList<FestivalOccurrence> occurrences;
        try
        {
            occurrences = calculator.Calculate(new FestivalCalculationRequest(
                first, last, new GeoCoordinate(location.Latitude, location.Longitude), location.TimeZone, rules.Definitions));
        }
        catch (SunEventNotFoundException exception)
        {
            throw new BusinessRuleException(
                "Festivals cannot be calculated for this location and date because the sun does not rise or set.", exception);
        }

        var count = Period.Between(first, last, PeriodUnits.Days).Days + 1;
        LogRangeCalculated(count, Stopwatch.GetElapsedTime(started).TotalMilliseconds, location.TimeZone.Id);

        var zone = location.TimeZone;
        var byDate = occurrences.ToLookup(occurrence => occurrence.Date);
        var days = Enumerable.Range(0, count)
            .Select(offset => first.PlusDays(offset))
            .Select(date => new FestivalDayData(date.ToDateOnly(), byDate[date].Select(occurrence => ToData(occurrence, zone)).ToArray()))
            .ToArray();

        // Shared work: not tied to any single caller's cancellation token.
        var now = clock.GetCurrentInstant();
        await Task.WhenAll(days.Select(day =>
        {
            var date = LocalDate.FromDateOnly(day.Date);
            var expiration = LocalDayCacheExpiration.Calculate(
                date, zone, now, options.Value.PastDateCacheLifetime, options.Value.MaxCacheLifetime);
            return cache.SetAsync(cacheKeys.Create(location, rules.Version, date), day, expiration, CancellationToken.None);
        }));

        return days;
    }

    private static FestivalOccurrenceData ToData(FestivalOccurrence occurrence, DateTimeZone zone) =>
        new(
            occurrence.Key,
            occurrence.Basis.ToString(),
            occurrence.BasisKey,
            ToLocal(occurrence.BasisStart, zone),
            ToLocal(occurrence.BasisEnd, zone),
            occurrence.Time.ToString(),
            occurrence.WindowStart is { } start ? ToLocal(start, zone) : null,
            occurrence.WindowEnd is { } end ? ToLocal(end, zone) : null);

    /// <summary>Rounds to the nearest second, like the Panchang times.</summary>
    private static DateTimeOffset ToLocal(Instant instant, DateTimeZone zone)
    {
        var rounded = Instant.FromUnixTimeSeconds((long)Math.Round(instant.ToUnixTimeTicks() / (double)NodaConstants.TicksPerSecond));
        return rounded.InZone(zone).ToDateTimeOffset();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Calculated festivals for {DayCount} days in {CalculationDurationMs:0.00} ms for {TimeZone}")]
    private partial void LogRangeCalculated(int dayCount, double calculationDurationMs, string timeZone);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resolved festivals for {DayCount} days in {TimeZone}: {CacheHits} cache hits, {CacheMisses} calculated")]
    private partial void LogDaysResolved(int dayCount, int cacheHits, int cacheMisses, string timeZone);
}
