using System.Diagnostics.Metrics;

namespace MyVedicStar.Shared.Caching;

/// <summary>Cache hit and miss counters, from which the hit ratio is derived.</summary>
public sealed class CacheMetrics
{
    /// <summary>Meter name to enable in OpenTelemetry.</summary>
    public const string MeterName = "MyVedicStar.Caching";

    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;

    public CacheMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _hits = meter.CreateCounter<long>("myvedicstar.cache.hits", unit: "{lookup}", description: "Cache lookups that found an entry.");
        _misses = meter.CreateCounter<long>("myvedicstar.cache.misses", unit: "{lookup}", description: "Cache lookups that found no entry.");
    }

    internal void RecordHit() => _hits.Add(1);

    internal void RecordMiss() => _misses.Add(1);
}
