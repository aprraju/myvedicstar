using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace MyVedicDay.Caching;

/// <summary>
/// <see cref="IDistributedObjectCache"/> over <see cref="IDistributedCache"/> with JSON serialization.
/// </summary>
internal sealed partial class DistributedObjectCache(IDistributedCache cache, ILogger<DistributedObjectCache> logger)
    : IDistributedObjectCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var started = Stopwatch.GetTimestamp();
        byte[]? payload;
        try
        {
            payload = await cache.GetAsync(key, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCacheReadFailed(exception);
            return null;
        }

        LogCacheRead(payload is not null, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        if (payload is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, SerializerOptions);
        }
        catch (JsonException exception)
        {
            // An entry written by an incompatible model version is treated as a miss and overwritten on the next set.
            LogCacheEntryUnreadable(exception);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, DateTimeOffset absoluteExpiration, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var payload = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        var options = new DistributedCacheEntryOptions { AbsoluteExpiration = absoluteExpiration };

        try
        {
            await cache.SetAsync(key, payload, options, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCacheWriteFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Distributed cache read (hit: {CacheHit}) in {DurationMs:0.0} ms")]
    private partial void LogCacheRead(bool cacheHit, double durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Distributed cache read failed; treating as a cache miss")]
    private partial void LogCacheReadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Distributed cache entry could not be deserialized; treating as a cache miss")]
    private partial void LogCacheEntryUnreadable(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Distributed cache write failed; continuing without caching")]
    private partial void LogCacheWriteFailed(Exception exception);
}
