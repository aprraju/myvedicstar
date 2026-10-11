using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MyVedicDay.Caching.Tests.Unit;

/// <summary>The cache never fails a request: errors degrade to a miss (reads) or a no-op (writes).</summary>
public sealed class DistributedObjectCacheTests
{
    private static readonly DateTimeOffset Expiry = DateTimeOffset.UtcNow.AddHours(1);

    [Fact]
    public async Task StoredValue_IsReturned_AndAMissIsNull()
    {
        var cache = Create(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

        await cache.SetAsync("day:1", new Entry("Rohini", 4), Expiry, CancellationToken.None);

        Assert.Equal(new Entry("Rohini", 4), await cache.GetAsync<Entry>("day:1", CancellationToken.None));
        Assert.Null(await cache.GetAsync<Entry>("day:2", CancellationToken.None));
    }

    [Fact]
    public async Task UnavailableCache_ReadsAsAMiss_AndWritesAreSkipped()
    {
        var cache = Create(new FailingCache(new InvalidOperationException("Redis is down")));

        Assert.Null(await cache.GetAsync<Entry>("day:1", CancellationToken.None));
        await cache.SetAsync("day:1", new Entry("Rohini", 4), Expiry, CancellationToken.None);
    }

    [Fact]
    public async Task EntryFromAnIncompatibleModel_IsAMiss()
    {
        var inner = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        await inner.SetAsync("day:1", Encoding.UTF8.GetBytes("not json"), new DistributedCacheEntryOptions());

        Assert.Null(await Create(inner).GetAsync<Entry>("day:1", CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        var cache = Create(new FailingCache(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(() => cache.GetAsync<Entry>("day:1", CancellationToken.None));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            cache.SetAsync("day:1", new Entry("Rohini", 4), Expiry, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BlankKey_IsRejected(string key)
    {
        var cache = Create(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

        await Assert.ThrowsAsync<ArgumentException>(() => cache.GetAsync<Entry>(key, CancellationToken.None));
    }

    private static DistributedObjectCache Create(IDistributedCache inner) => new(inner, NullLogger<DistributedObjectCache>.Instance);

    public sealed record Entry(string Name, int Number);
}
