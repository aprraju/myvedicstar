using MyVedicStar.Caching;

namespace MyVedicDay.Caching;

internal sealed class HybridObjectCache(IApplicationCache cache) : IDistributedObjectCache
{
    public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => cache.GetAsync<T>(key, token);
    public Task SetAsync<T>(string key, T value, DateTimeOffset expiry, CancellationToken token) where T : class => cache.SetAsync(key, value, expiry, token);
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, DateTimeOffset expiry, CancellationToken token) where T : class => cache.GetOrCreateAsync(key, factory, expiry, token);
    public Task<T> GetOrCreateWithPolicyAsync<T>(string key, string policy, Func<CancellationToken, Task<T>> factory, DateTimeOffset expiry, CancellationToken token) where T : class => cache.GetOrCreateAsync(key, factory, expiry, token, policy);
}
