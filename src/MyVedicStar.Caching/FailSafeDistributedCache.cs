using System.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace MyVedicStar.Caching;

/// <summary>Bounds provider work and backs off on cache outages. It does not implement storage or SQL.</summary>
internal sealed class FailSafeDistributedCache(IDistributedCache provider, ApplicationCacheOptions options, ApplicationCacheMetrics metrics, TimeProvider time, ILogger<FailSafeDistributedCache> logger) : IDistributedCache, IDisposable
{
    private readonly SemaphoreSlim _gate = new(options.L2.MaximumConcurrentOperations);
    private long _retryAfter;

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, T fallback, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (time.GetUtcNow().Ticks < Volatile.Read(ref _retryAfter)) return fallback;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(options.L2.CommandTimeoutSeconds));
        var entered = false;
        var start = Stopwatch.GetTimestamp();
        try
        {
            await _gate.WaitAsync(budget.Token);
            entered = true;
            return await operation(budget.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            var now = time.GetUtcNow().Ticks;
            Interlocked.Exchange(ref _retryAfter, now + options.L2.FailureBackoff.Ticks);
            metrics.RecordBackendFailure(now);
            metrics.Failures.Add(1);
            logger.LogWarning("PostgreSQL cache operation failed ({ExceptionType}); using calculation fallback and backing off", exception.GetType().Name);
            return fallback;
        }
        finally { metrics.DatabaseMilliseconds.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds); if (entered) _gate.Release(); }
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => ExecuteAsync(async ct =>
    {
        var payload = await provider.GetAsync(key, ct);
        if (payload is not null) metrics.L2Hits.Add(1);
        return payload;
    }, (byte[]?)null, token);
    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions entryOptions, CancellationToken token = default) => await ExecuteAsync(async ct => { await provider.SetAsync(key, value, entryOptions, ct); metrics.Writes.Add(1); return true; }, false, token);
    public async Task RemoveAsync(string key, CancellationToken token = default) => await ExecuteAsync(async ct => { await provider.RemoveAsync(key, ct); return true; }, false, token);
    public async Task RefreshAsync(string key, CancellationToken token = default) => await ExecuteAsync(async ct => { await provider.RefreshAsync(key, ct); return true; }, false, token);
    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();
    public void Set(string key, byte[] value, DistributedCacheEntryOptions entryOptions) => SetAsync(key, value, entryOptions).GetAwaiter().GetResult();
    public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();
    public void Refresh(string key) => RefreshAsync(key).GetAwaiter().GetResult();
    public void Dispose() => _gate.Dispose();
}
