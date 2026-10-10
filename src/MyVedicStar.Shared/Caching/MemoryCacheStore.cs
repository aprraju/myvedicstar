using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Options;

namespace MyVedicStar.Shared.Caching;

/// <summary>
/// <see cref="ICacheStore"/> backed by a private <see cref="MemoryCache"/> in this API instance.
/// </summary>
/// <remarks>
/// The cache is private rather than the shared <see cref="IMemoryCache"/> service: its entry limit requires every
/// entry to declare a size, which other libraries using the shared cache do not do.
/// </remarks>
internal sealed class MemoryCacheStore : ICacheStore, IDisposable
{
    private const long EntrySize = 1;

    private readonly MemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly CacheMetrics _metrics;

    // One pending factory call per key, shared by every caller that misses while it runs.
    private readonly ConcurrentDictionary<string, Lazy<Task<object>>> _pendingLoads = new(StringComparer.Ordinal);

    public MemoryCacheStore(IOptions<CachingOptions> options, TimeProvider timeProvider, CacheMetrics metrics)
    {
        _timeProvider = timeProvider;
        _metrics = metrics;
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = options.Value.MaxEntries,
            CompactionPercentage = options.Value.CompactionPercentage,
            ExpirationScanFrequency = options.Value.ExpirationScanFrequency,
            Clock = new TimeProviderClock(timeProvider),
        });
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        where T : class
    {
        ValidateKey(key);
        return Task.FromResult(TryGet<T>(key, out var value) ? value : null);
    }

    public Task SetAsync<T>(string key, T value, DateTimeOffset absoluteExpiration, CancellationToken cancellationToken)
        where T : class
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);

        Store(key, value, absoluteExpiration);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ValidateKey(key);

        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        DateTimeOffset absoluteExpiration,
        CancellationToken cancellationToken)
        where T : class
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (TryGet<T>(key, out var cached))
        {
            return cached;
        }

        var pendingLoad = _pendingLoads.GetOrAdd(
            key,
            _ => new Lazy<Task<object>>(() => LoadAsync(key, factory, absoluteExpiration), LazyThreadSafetyMode.ExecutionAndPublication));

        return (T)await pendingLoad.Value.WaitAsync(cancellationToken);
    }

    public void Dispose() => _cache.Dispose();

    private async Task<object> LoadAsync<T>(string key, Func<CancellationToken, Task<T>> factory, DateTimeOffset absoluteExpiration)
        where T : class
    {
        try
        {
            // Another load for this key may have finished between the caller's miss and this load starting.
            if (_cache.TryGetValue(key, out var existing) && existing is T existingValue)
            {
                return existingValue;
            }

            var value = await factory(CancellationToken.None)
                ?? throw new InvalidOperationException($"The cache factory for a {typeof(T).Name} returned null.");

            Store(key, value, absoluteExpiration);
            return value;
        }
        finally
        {
            _pendingLoads.TryRemove(key, out _);
        }
    }

    private bool TryGet<T>(string key, out T value)
        where T : class
    {
        if (_cache.TryGetValue(key, out var entry) && entry is T typed)
        {
            _metrics.RecordHit();
            value = typed;
            return true;
        }

        _metrics.RecordMiss();
        value = null!;
        return false;
    }

    private void Store<T>(string key, T value, DateTimeOffset absoluteExpiration)
        where T : class
    {
        if (absoluteExpiration <= _timeProvider.GetUtcNow())
        {
            _cache.Remove(key);
            return;
        }

        _cache.Set(key, value, new MemoryCacheEntryOptions { AbsoluteExpiration = absoluteExpiration, Size = EntrySize });
    }

    private static void ValidateKey(string key) => ArgumentException.ThrowIfNullOrWhiteSpace(key);

    /// <summary>Lets the cache's expiration follow <see cref="TimeProvider"/>, so tests can control time.</summary>
    private sealed class TimeProviderClock(TimeProvider timeProvider) : ISystemClock
    {
        public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
    }
}
