using Microsoft.Extensions.Logging;
using MyVedicStar.Caching;
using MyVedicStar.Kundali.Business;

namespace MyVedicStar.Kundali.Integration;

/// <summary>
/// Adapts the shared application cache to the Kundali port. It holds no policy of its own: the key, the validity and the
/// cache policy are decided by the business layer, and this class only translates calls.
/// </summary>
/// <remarks>
/// Failures are a miss, never an error: the persisted result is the source of truth (KUN-04), so the cache is safe to
/// flush entirely and an unavailable cache must not take a section read down with it. A read failure is logged at Debug
/// (expected during a backend outage) so log volume does not become the outage.
/// </remarks>
internal sealed class KundaliResultCacheAdapter(IApplicationCache cache, ILogger<KundaliResultCacheAdapter> log)
    : IKundaliResultCache
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
    {
        try
        {
            return await cache.GetAsync<T>(key, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogDebug(ex, "Kundali cache read failed; treating it as a miss.");
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, DateTimeOffset validUntil, string policy,
        CancellationToken token) where T : class
    {
        try
        {
            await cache.SetAsync(key, value, validUntil, token);
            _ = policy;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogDebug(ex, "Kundali cache write failed; the persisted result remains authoritative.");
        }
    }

    public async Task RemoveAsync(string key, CancellationToken token)
    {
        try
        {
            await cache.RemoveAsync(key, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogDebug(ex, "Kundali cache remove failed; the entry expires on its own.");
        }
    }
}
