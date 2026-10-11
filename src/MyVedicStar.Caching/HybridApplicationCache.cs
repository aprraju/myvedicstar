using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MyVedicStar.Caching;

internal sealed class HybridApplicationCache(HybridCache cache, IOptions<ApplicationCacheOptions> options, TimeProvider time, ApplicationCacheMetrics metrics, ILogger<HybridApplicationCache> logger) : IApplicationCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Envelope<T>(DateTimeOffset ValidUntil, T Value);
    private sealed class SerializationFailure(object value, Exception inner) : Exception("Cache serialization failed", inner) { internal object Value { get; } = value; }

    private HybridCacheEntryOptions EntryOptions(string key, DateTimeOffset validUntil, HybridCacheEntryFlags extra = HybridCacheEntryFlags.None, string? policyName = null)
    {
        var settings = options.Value;
        var category = policyName ?? key.Split(':', 2)[0];
        var policy = settings.Policies.GetValueOrDefault(category) ?? settings.Policies["default"];
        var remaining = validUntil - time.GetUtcNow();
        var ttl = remaining < policy.DistributedLifetime ? remaining : policy.DistributedLifetime;
        if (ttl <= TimeSpan.Zero) ttl = TimeSpan.FromTicks(1);
        var memory = ttl < policy.MemoryLifetime ? ttl : policy.MemoryLifetime;
        var flags = extra;
        if (!settings.MemoryEnabled || memory == TimeSpan.Zero) flags |= HybridCacheEntryFlags.DisableLocalCache;
        if (!settings.L2.Enabled) flags |= HybridCacheEntryFlags.DisableDistributedCache;
        return new HybridCacheEntryOptions { Expiration = ttl, LocalCacheExpiration = memory, Flags = flags };
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Value.Enabled) return null;
        var storageKey = CacheKeyBuilder.StorageKey<T>(key, options.Value.KeyVersion);
        var settings = EntryOptions(key, DateTimeOffset.MaxValue, HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite);
        var start = Stopwatch.GetTimestamp();
        metrics.Requests.Add(1);
        try
        {
            var payload = await cache.GetOrCreateAsync<byte[]?>(storageKey, _ => new ValueTask<byte[]?>((byte[]?)null), settings, tags: [CacheKeyBuilder.InvalidationTag(key, options.Value.KeyVersion)], cancellationToken: cancellationToken);
            return payload is null ? null : Decode<T>(payload);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidDataException)
        {
            RecordFailure(exception); await cache.RemoveAsync(storageKey, cancellationToken); return null;
        }
        finally { metrics.LookupMilliseconds.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, DateTimeOffset validUntil, CancellationToken cancellationToken = default, string? policy = null) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Value.Enabled || validUntil <= time.GetUtcNow()) return await factory(cancellationToken);
        var storageKey = CacheKeyBuilder.StorageKey<T>(key, options.Value.KeyVersion);
        var entryOptions = EntryOptions(key, validUntil, policyName: policy);
        metrics.Requests.Add(1);
        var start = Stopwatch.GetTimestamp();
        async ValueTask<byte[]> Load(CancellationToken token)
        {
            metrics.FactoryCalls.Add(1);
            var calculationStart = Stopwatch.GetTimestamp();
            T value;
            try { value = await factory(token); }
            finally { metrics.CalculationMilliseconds.Record(Stopwatch.GetElapsedTime(calculationStart).TotalMilliseconds); }
            if (validUntil <= time.GetUtcNow())
                throw new SerializationFailure(value, new InvalidDataException("Result validity ended during calculation; returning it without caching."));
            var expires = time.GetUtcNow() + entryOptions.Expiration!.Value;
            if (validUntil < expires) expires = validUntil;
            try { return Encode(value, expires); }
            catch (Exception exception) when (exception is not OperationCanceledException) { throw new SerializationFailure(value, exception); }
        }
        try
        {
            var payload = await cache.GetOrCreateAsync(storageKey, Load, entryOptions, tags: [CacheKeyBuilder.InvalidationTag(key, options.Value.KeyVersion)], cancellationToken: cancellationToken);
            try
            {
                var value = Decode<T>(payload);
                if (value is not null) return value;
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidDataException) { RecordFailure(exception); }
            await cache.RemoveAsync(storageKey, cancellationToken);
            var bypass = EntryOptions(key, validUntil, HybridCacheEntryFlags.DisableLocalCacheRead | HybridCacheEntryFlags.DisableDistributedCacheRead, policy);
            payload = await cache.GetOrCreateAsync(storageKey, Load, bypass, tags: [CacheKeyBuilder.InvalidationTag(key, options.Value.KeyVersion)], cancellationToken: cancellationToken);
            return Decode<T>(payload) ?? throw new InvalidDataException("Calculation returned an expired cache entry.");
        }
        catch (SerializationFailure failure) { RecordFailure(failure.InnerException!); return (T)failure.Value; }
        finally { metrics.LookupMilliseconds.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
    }

    public async Task SetAsync<T>(string key, T value, DateTimeOffset validUntil, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Value.Enabled) return;
        var storageKey = CacheKeyBuilder.StorageKey<T>(key, options.Value.KeyVersion);
        if (validUntil <= time.GetUtcNow()) { await cache.RemoveAsync(storageKey, cancellationToken); return; }
        var entryOptions = EntryOptions(key, validUntil);
        var expires = time.GetUtcNow() + entryOptions.Expiration!.Value;
        if (validUntil < expires) expires = validUntil;
        try { await cache.SetAsync(storageKey, Encode(value, expires), entryOptions, tags: [CacheKeyBuilder.InvalidationTag(key, options.Value.KeyVersion)], cancellationToken: cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException) { RecordFailure(exception); }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        await cache.RemoveByTagAsync(CacheKeyBuilder.InvalidationTag(key, options.Value.KeyVersion), cancellationToken);

    public async Task RemoveAsync<T>(string key, CancellationToken cancellationToken = default) where T : class =>
        await cache.RemoveAsync(CacheKeyBuilder.StorageKey<T>(key, options.Value.KeyVersion), cancellationToken);

    private byte[] Encode<T>(T value, DateTimeOffset expiry)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new Envelope<T>(expiry, value), Json);
            if (payload.Length > options.Value.MaximumPayloadBytes) throw new InvalidDataException("Cache payload exceeds the configured limit.");
            return payload;
        }
        finally { metrics.SerializationMilliseconds.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    }
    private T? Decode<T>(byte[] payload) where T : class
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var entry = JsonSerializer.Deserialize<Envelope<T>>(payload, Json) ?? throw new JsonException("Invalid cache envelope.");
            return entry.ValidUntil > time.GetUtcNow() ? entry.Value : null;
        }
        finally { metrics.SerializationMilliseconds.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    }
    private void RecordFailure(Exception exception)
    {
        metrics.Failures.Add(1);
        logger.LogWarning("Cache payload could not be used ({ExceptionType}); using calculation fallback", exception.GetType().Name);
    }
}
