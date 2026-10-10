namespace MyVedicStar.Shared.Caching;

/// <summary>
/// Application cache for computed values, such as daily Panchang results. Domain code wraps it in its own
/// abstraction (for example <c>IPanchangCache</c>) that owns key building and expiration rules.
/// </summary>
/// <remarks>
/// The current implementation is an in-memory cache held by each API instance, so entries are not shared between
/// replicas and are lost on restart. The contract is asynchronous so a distributed implementation can replace it
/// without changing callers. A key must always hold values of the same type.
/// </remarks>
public interface ICacheStore
{
    /// <summary>Returns the cached value, or <see langword="null"/> if the key is absent or expired.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        where T : class;

    /// <summary>
    /// Stores <paramref name="value"/> until <paramref name="absoluteExpiration"/>, replacing any existing entry.
    /// An expiration that has already passed removes the entry instead.
    /// </summary>
    Task SetAsync<T>(string key, T value, DateTimeOffset absoluteExpiration, CancellationToken cancellationToken)
        where T : class;

    /// <summary>Removes the entry, if present.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Cache-aside read: returns the cached value, or runs <paramref name="factory"/>, caches its result until
    /// <paramref name="absoluteExpiration"/> and returns it.
    /// </summary>
    /// <remarks>
    /// Concurrent misses for the same key share one factory call (stampede protection within this instance).
    /// The factory receives <see cref="CancellationToken.None"/>, because its result is shared by every waiting
    /// caller; <paramref name="cancellationToken"/> only stops this caller from waiting. A factory exception is
    /// passed to every waiting caller and nothing is cached.
    /// </remarks>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        DateTimeOffset absoluteExpiration,
        CancellationToken cancellationToken)
        where T : class;
}
