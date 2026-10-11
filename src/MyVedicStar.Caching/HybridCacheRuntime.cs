using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MyVedicStar.Caching;

/// <summary>Owns a dedicated, byte-bounded native HybridCache L1 without imposing size requirements on other application memory caches.</summary>
internal sealed class HybridCacheRuntime : IAsyncDisposable, IDisposable
{
    private readonly ServiceProvider _services;
    internal MemoryCache Memory { get; }
    internal HybridCache Cache { get; }
    public HybridCacheRuntime(ApplicationCacheOptions options, ApplicationCacheMetrics metrics, ILoggerFactory logging, TimeProvider time, IDistributedCache? backend)
    {
        Memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.MemoryLimitBytes, TrackStatistics = true });
        metrics.ObserveMemory(Memory);
        var services = new ServiceCollection();
        services.AddSingleton(logging);
        services.AddSingleton(time);
        services.AddSingleton<IMemoryCache>(new MeteredMemoryCache(Memory, metrics));
        if (backend is not null) services.AddSingleton(backend);
        services.AddHybridCache(settings =>
        {
            settings.MaximumPayloadBytes = options.MaximumPayloadBytes;
            settings.MaximumKeyLength = 256;
        });
        _services = services.BuildServiceProvider();
        Cache = _services.GetRequiredService<HybridCache>();
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public async ValueTask DisposeAsync() { await _services.DisposeAsync(); Memory.Dispose(); }

    private sealed class MeteredMemoryCache(MemoryCache memory, ApplicationCacheMetrics metrics) : IMemoryCache
    {
        public bool TryGetValue(object key, out object? value)
        {
            var hit = memory.TryGetValue(key, out value);
            if (hit) metrics.L1Hits.Add(1);
            return hit;
        }
        public ICacheEntry CreateEntry(object key) => memory.CreateEntry(key);
        public void Remove(object key) => memory.Remove(key);
        public void Dispose() { }
    }
}
