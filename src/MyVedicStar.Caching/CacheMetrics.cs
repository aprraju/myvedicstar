using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;

namespace MyVedicStar.Caching;

public sealed class ApplicationCacheMetrics
{
    public const string MeterName = "MyVedicStar.HybridCaching";
    internal readonly Counter<long> Requests;
    internal readonly Counter<long> FactoryCalls;
    internal readonly Counter<long> Failures;
    internal readonly Counter<long> L1Hits;
    internal readonly Counter<long> L2Hits;
    internal readonly Counter<long> Writes;
    internal readonly Histogram<double> LookupMilliseconds;
    internal readonly Histogram<double> SerializationMilliseconds;
    internal readonly Histogram<double> CalculationMilliseconds;
    internal readonly Histogram<double> DatabaseMilliseconds;
    private readonly Meter _meter;
    private long _lastBackendFailureTicks;
    public long LastBackendFailureTicks => Interlocked.Read(ref _lastBackendFailureTicks);
    internal void RecordBackendFailure(long ticks) => Interlocked.Exchange(ref _lastBackendFailureTicks, ticks);
    internal void ObserveMemory(MemoryCache memory)
    {
        _meter.CreateObservableGauge("cache.l1.estimated_size", () => memory.GetCurrentStatistics()?.CurrentEstimatedSize ?? 0, "By");
        _meter.CreateObservableGauge("cache.l1.entries", () => memory.GetCurrentStatistics()?.CurrentEntryCount ?? 0, "{entry}");
    }

    public ApplicationCacheMetrics(IMeterFactory factory)
    {
        var meter = _meter = factory.Create(MeterName);
        Requests = meter.CreateCounter<long>("cache.requests");
        FactoryCalls = meter.CreateCounter<long>("cache.factory.calls");
        Failures = meter.CreateCounter<long>("cache.failures");
        L1Hits = meter.CreateCounter<long>("cache.l1.lookups.hits");
        L2Hits = meter.CreateCounter<long>("cache.postgres.read.hits");
        Writes = meter.CreateCounter<long>("cache.postgres.writes");
        LookupMilliseconds = meter.CreateHistogram<double>("cache.lookup.duration", "ms");
        SerializationMilliseconds = meter.CreateHistogram<double>("cache.serialization.duration", "ms");
        CalculationMilliseconds = meter.CreateHistogram<double>("cache.factory.duration", "ms");
        DatabaseMilliseconds = meter.CreateHistogram<double>("cache.postgres.duration", "ms");
    }
}
