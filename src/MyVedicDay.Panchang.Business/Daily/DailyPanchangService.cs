using MyVedicDay.Panchang.Caching;
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
    PanchangMetrics metrics,
    ILogger<DailyPanchangService> logger,
    IPanchangCalculationCache? calculations = null,
    IClock? clock = null)
{
    public async Task<IReadOnlyList<DailyPanchangData>> GetDaysAsync(
        PanchangRequestContext context, IReadOnlyList<LocalDate> dates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dates);

        var results = await Task.WhenAll(dates.Select(date => cache.GetOrCreateWithPolicyAsync(
            cacheKeys.Create(context, date),
            dates.Count > 1 ? "calendar" : date < (clock ?? SystemClock.Instance).GetCurrentInstant().InZone(context.Location.TimeZone).Date ? "historical" : "panchang",
            token => CalculateAsync(context, date, token),
            cacheExpiration.GetAbsoluteExpiration(date, context.Location.TimeZone),
            cancellationToken)));
        return results;
    }

    private async Task<DailyPanchangData> CalculateAsync(PanchangRequestContext context, LocalDate date, CancellationToken token)
    {
        var location = context.Location;
        var request = new PanchangCalculationRequest(
            date, new GeoCoordinate(location.Latitude, location.Longitude), location.TimeZone, context.CalculationMethod);

        var started = Stopwatch.GetTimestamp();
        PanchangDay day;
        try
        {
            day = calculations is null ? calculator.Calculate(request) : await calculations.CalculateAsync(request, token);
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
