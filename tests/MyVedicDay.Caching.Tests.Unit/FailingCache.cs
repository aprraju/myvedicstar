using Microsoft.Extensions.Caching.Distributed;

namespace MyVedicDay.Caching.Tests.Unit;

/// <summary>A distributed cache whose every call fails, standing in for an unreachable Redis.</summary>
internal sealed class FailingCache(Exception failure) : IDistributedCache
{
    public byte[]? Get(string key) => throw failure;

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromException<byte[]?>(failure);

    public void Refresh(string key) => throw failure;

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.FromException(failure);

    public void Remove(string key) => throw failure;

    public Task RemoveAsync(string key, CancellationToken token = default) => Task.FromException(failure);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw failure;

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        Task.FromException(failure);
}
