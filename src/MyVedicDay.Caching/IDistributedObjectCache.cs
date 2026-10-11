namespace MyVedicDay.Caching;

/// <summary>
/// Distributed cache for any serializable result. Keys and expirations are decided by the calling domain;
/// this abstraction only stores and retrieves serialized values.
/// </summary>
/// <remarks>
/// Cache failures degrade to a miss (reads) or a no-op (writes) so that an unavailable cache never fails a request.
/// </remarks>
public interface IDistributedObjectCache
{
    /// <summary>Returns the cached value, or <see langword="null"/> on a miss, an unreadable entry, or a cache failure.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        where T : class;

    /// <summary>Stores a value until <paramref name="absoluteExpiration"/>.</summary>
    Task SetAsync<T>(string key, T value, DateTimeOffset absoluteExpiration, CancellationToken cancellationToken)
        where T : class;
    /// <summary>Applies a configured policy while keeping the same deterministic data key.</summary>
    Task<T> GetOrCreateWithPolicyAsync<T>(string key, string policy, Func<CancellationToken, Task<T>> factory, DateTimeOffset expiry, CancellationToken token) where T : class => GetOrCreateAsync(key, factory, expiry, token);
    /// <summary>Factory-based caching; production uses HybridCache's native stampede protection.</summary>
    async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, DateTimeOffset expiry, CancellationToken token) where T : class
    {
        if (await GetAsync<T>(key, token) is { } hit) return hit;
        var value = await factory(token);
        await SetAsync(key, value, expiry, token);
        return value;
    }
}
