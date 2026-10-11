using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MyVedicDay.Astronomy;
using MyVedicDay.Caching;
using MyVedicDay.Panchang.Business.Caching;
using MyVedicDay.Panchang.Business.Diagnostics;
using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Calculation.Models;
using MyVedicDay.Shared.Exceptions;
using NodaTime;

namespace MyVedicDay.Panchang.Business.Daily;

/// <summary>
/// Cache-aside retrieval of daily Panchang entries. Multi-day requests reuse cached days and calculate only the
/// missing ones; concurrent misses for the same key are coalesced.
/// </summary>
internal sealed partial class DailyPanchangService(
    IPanchangCalculator calculator,
    IDistributedObjectCache cache,
    PanchangCacheKeyFactory cacheKeys,
    PanchangCacheExpirationPolicy cacheExpiration,
    InFlightRequestCoalescer<DailyPanchangData> coalescer,
    PanchangMetrics metrics,
    ILogger<DailyPanchangService> logger)
{
    public async Task<IReadOnlyList<DailyPanchangData>> GetDaysAsync(
        PanchangRequestContext context, IReadOnlyList<LocalDate> dates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dates);

        var keys = dates.Select(date => cacheKeys.Create(context, date)).ToArray();
        var cached = await Task.WhenAll(keys.Select(key => cache.GetAsync<DailyPanchangData>(key, cancellationToken)));

        var results = new DailyPanchangData[dates.Count];
        var misses = new List<(int Index, Task<DailyPanchangData> Calculation)>();
        for (var i = 0; i < dates.Count; i++)
        {
            // Guard against a mismatched entry ever being served for another date.
            if (cached[i] is { } hit && hit.Date == dates[i].ToDateOnly())
            {
                results[i] = hit;
                continue;
            }

            var date = dates[i];
            var key = keys[i];
            misses.Add((i, coalescer.GetOrStartAsync(key, () => CalculateAndCacheAsync(context, date, key))));
        }

        metrics.RecordCacheLookups(dates.Count - misses.Count, misses.Count);

        foreach (var (index, calculation) in misses)
        {
            results[index] = await calculation.WaitAsync(cancellationToken);
        }

        LogDaysResolved(dates.Count, dates.Count - misses.Count, misses.Count, context.Location.TimeZone.Id, context.CalculationMethod);
        return results;
    }

    private async Task<DailyPanchangData> CalculateAndCacheAsync(PanchangRequestContext context, LocalDate date, string key)
    {
        var day = Calculate(context, date);

        // Shared work: not tied to any single caller's cancellation token.
        await cache.SetAsync(key, day, cacheExpiration.GetAbsoluteExpiration(date, context.Location.TimeZone), CancellationToken.None);
        return day;
    }

    private DailyPanchangData Calculate(PanchangRequestContext context, LocalDate date)
    {
        var location = context.Location;
        var request = new PanchangCalculationRequest(
            date, new GeoCoordinate(location.Latitude, location.Longitude), location.TimeZone, context.CalculationMethod);

        var started = Stopwatch.GetTimestamp();
        PanchangDay day;
        try
        {
            day = calculator.Calculate(request);
        }
        catch (SunEventNotFoundException exception)
        {
            throw new BusinessRuleException(
                "The Panchang cannot be calculated for this location and date because the sun does not rise or set.", exception);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        metrics.RecordCalculation(elapsed);
        LogDayCalculated(elapsed.TotalMilliseconds, location.TimeZone.Id);

        return DailyPanchangMapper.ToData(day);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Calculated daily Panchang in {CalculationDurationMs:0.00} ms for {TimeZone}")]
    private partial void LogDayCalculated(double calculationDurationMs, string timeZone);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resolved {DayCount} Panchang days for {TimeZone} ({CalculationMethod}): {CacheHits} cache hits, {CacheMisses} calculated")]
    private partial void LogDaysResolved(int dayCount, int cacheHits, int cacheMisses, string timeZone, string calculationMethod);
}
