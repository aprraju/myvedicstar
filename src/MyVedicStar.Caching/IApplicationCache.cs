namespace MyVedicStar.Caching;

/// <summary>Reusable safe-data cache. Authentication responses and authorization decisions must not use this service.</summary>
public interface IApplicationCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    Task SetAsync<T>(string key, T value, DateTimeOffset validUntil, CancellationToken cancellationToken = default) where T : class;
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, DateTimeOffset validUntil, CancellationToken cancellationToken = default, string? policy = null) where T : class;
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
}
