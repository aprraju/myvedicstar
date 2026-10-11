using System.Collections.Concurrent;
using MyVedicDay.Caching;

namespace MyVedicDay.Panchang.Tests.Unit.TestDoubles;

/// <summary>
/// Dictionary-backed cache that records expirations but never evicts, so tests using a fake clock are deterministic.
/// </summary>
internal sealed class InMemoryPanchangCache : IDistributedObjectCache
{
    private readonly ConcurrentDictionary<string, object> _entries = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, DateTimeOffset> Expirations { get; } = new(StringComparer.Ordinal);

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        where T : class =>
        Task.FromResult(_entries.TryGetValue(key, out var value) ? value as T : null);

    public Task SetAsync<T>(string key, T value, DateTimeOffset absoluteExpiration, CancellationToken cancellationToken)
        where T : class
    {
        _entries[key] = value;
        Expirations[key] = absoluteExpiration;
        return Task.CompletedTask;
    }
}
