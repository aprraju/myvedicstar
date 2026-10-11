using System.Diagnostics.Metrics;

namespace MyVedicDay.Panchang.Business.Diagnostics;

/// <summary>
/// Telemetry identifiers the host subscribes to (for example via OpenTelemetry).
/// </summary>
public static class PanchangTelemetry
{
    public const string MeterName = "MyVedicDay.Panchang";
}

/// <summary>
/// Panchang business metrics: cache hit ratio and calculation latency.
/// </summary>
internal sealed class PanchangMetrics
{
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Histogram<double> _calculationDuration;

    public PanchangMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(PanchangTelemetry.MeterName);
        _cacheHits = meter.CreateCounter<long>("panchang.cache.hits", "{day}", "Daily Panchang entries served from cache.");
        _cacheMisses = meter.CreateCounter<long>("panchang.cache.misses", "{day}", "Daily Panchang entries not found in cache.");
        _calculationDuration = meter.CreateHistogram<double>(
            "panchang.calculation.duration", "ms", "Time to calculate one daily Panchang.");
    }

    public void RecordCacheLookups(int hits, int misses)
    {
        _cacheHits.Add(hits);
        _cacheMisses.Add(misses);
    }

    public void RecordCalculation(TimeSpan duration) => _calculationDuration.Record(duration.TotalMilliseconds);
}
