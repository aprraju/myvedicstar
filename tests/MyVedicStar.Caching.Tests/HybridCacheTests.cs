using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
namespace MyVedicStar.Caching.Tests;
public sealed class HybridCacheTests
{
    public sealed record Result(string Text, List<int>? Values = null);
    public sealed class Cycle { public Cycle Self => this; }
    internal sealed class Rig : IAsyncDisposable
    {
        private readonly ServiceProvider services = new ServiceCollection().AddLogging().AddMetrics().BuildServiceProvider();
        internal readonly HybridCacheRuntime Runtime;
        internal readonly IApplicationCache Cache;
        internal readonly ApplicationCacheMetrics Metrics;
        internal readonly ApplicationCacheOptions Options;
        internal readonly FakeTimeProvider Time;
        internal Rig(IDistributedCache? backend = null, ApplicationCacheOptions? settings = null, FakeTimeProvider? time = null)
        {
            Options = settings ?? new(); Options.L2.Enabled = backend is not null;
            Time = time ?? new FakeTimeProvider(DateTimeOffset.UtcNow);
            Metrics = new ApplicationCacheMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
            Runtime = new HybridCacheRuntime(Options, Metrics, NullLoggerFactory.Instance, Time, backend);
            Cache = new HybridApplicationCache(Runtime.Cache, Microsoft.Extensions.Options.Options.Create(Options), Time, Metrics, NullLogger<HybridApplicationCache>.Instance);
        }
        internal DateTimeOffset Expiry => Time.GetUtcNow().AddHours(1);
        public async ValueTask DisposeAsync() { await Runtime.DisposeAsync(); await services.DisposeAsync(); }
    }
    internal sealed class Backend : IDistributedCache
    {
        private readonly MemoryDistributedCache memory = new(Options.Create(new MemoryDistributedCacheOptions()));
        internal int Reads, Writes;
        internal bool Fail;
        public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) { Interlocked.Increment(ref Reads); if (Fail) throw new IOException("Unavailable"); return memory.GetAsync(key, token); }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) { Interlocked.Increment(ref Writes); if (Fail) throw new IOException("Unavailable"); return memory.SetAsync(key,value,options,token); }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => SetAsync(key,value,options).GetAwaiter().GetResult();
        public Task RemoveAsync(string key, CancellationToken token = default) => memory.RemoveAsync(key,token);
        public void Remove(string key) => memory.Remove(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => memory.RefreshAsync(key,token);
        public void Refresh(string key) => memory.Refresh(key);
    }
    [Fact]
    public async Task L1Hit_AvoidsDatabase_AndReturnsIndependentMutableValues()
    {
        var backend = new Backend(); await using var rig = new Rig(backend); var calls=0;
        Task<Result> Load(CancellationToken _) { calls++; return Task.FromResult(new Result("ok", [1])); }
        var first = await rig.Cache.GetOrCreateAsync("default:one",Load,rig.Expiry);
        var reads=backend.Reads; first.Values!.Add(99);
        var second = await rig.Cache.GetOrCreateAsync("default:one",Load,rig.Expiry);
        Assert.Equal(1,calls); Assert.Equal(reads,backend.Reads); Assert.Single(second.Values!); Assert.Equal(1,second.Values![0]); Assert.NotSame(first,second);
    }
    [Fact]
    public async Task SecondInstance_AndRestart_ReadPersistedL2WithoutFactory()
    {
        var backend=new Backend();
        await using(var first=new Rig(backend)) await first.Cache.SetAsync("default:shared",new Result("persistent"),first.Expiry);
        await using var restarted=new Rig(backend);
        var value=await restarted.Cache.GetOrCreateAsync<Result>("default:shared",_=>throw new InvalidOperationException("Must hit L2"),restarted.Expiry);
        Assert.Equal("persistent",value.Text);
    }
    [Fact]
    public async Task ZeroL1_UsesL2EveryTime()
    {
        var backend=new Backend(); await using var rig=new Rig(backend);
        await rig.Cache.GetOrCreateAsync("muhurta:zero",_=>Task.FromResult(new Result("ok")),rig.Expiry);
        var before=backend.Reads;
        await rig.Cache.GetOrCreateAsync<Result>("muhurta:zero",_=>throw new InvalidOperationException("L2 expected"),rig.Expiry);
        Assert.True(backend.Reads>before);
        Assert.Equal(0,rig.Runtime.Memory.GetCurrentStatistics()!.CurrentEntryCount);
    }
    [Fact]
    public async Task DisabledCache_AlwaysRunsFactory()
    {
        await using var rig=new Rig(settings:new ApplicationCacheOptions{Enabled=false}); var calls=0;
        for(var i=0;i<3;i++) await rig.Cache.GetOrCreateAsync("default:off",_=>Task.FromResult(new Result((++calls).ToString(System.Globalization.CultureInfo.InvariantCulture))),rig.Expiry);
        Assert.Equal(3,calls); Assert.Equal(0,rig.Runtime.Memory.Count);
    }
    [Fact]
    public async Task UnderlyingMidnightExpiry_IsRespectedEvenWithStaleL1()
    {
        await using var rig=new Rig(); var expiry=rig.Time.GetUtcNow().AddSeconds(1);
        await rig.Cache.SetAsync("panchang:today",new Result("yesterday"),expiry);
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        var value=await rig.Cache.GetOrCreateAsync("panchang:today",_=>Task.FromResult(new Result("today")),rig.Expiry);
        Assert.Equal("today",value.Text);
    }
    [Fact]
    public async Task ConcurrentMisses_UseNativeSingleFlight()
    {
        await using var rig=new Rig(); var calls=0; var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Result> Load(CancellationToken ct){Interlocked.Increment(ref calls);entered.TrySetResult();await release.Task.WaitAsync(ct);return new Result("one");}
        var requests=Enumerable.Range(0,20).Select(_=>rig.Cache.GetOrCreateAsync("default:stampede",Load,rig.Expiry)).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));release.SetResult();await Task.WhenAll(requests);Assert.Equal(1,calls);
    }
    [Fact]
    public async Task OneCancelledWaiter_DoesNotCancelOtherWaiter()
    {
        await using var rig=new Rig(); using var cancelled=new CancellationTokenSource();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Result> Load(CancellationToken ct){entered.TrySetResult();await release.Task.WaitAsync(ct);return new Result("survived");}
        var first=rig.Cache.GetOrCreateAsync("default:cancel",Load,rig.Expiry,cancelled.Token);await entered.Task;
        var second=rig.Cache.GetOrCreateAsync("default:cancel",Load,rig.Expiry);cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first.WaitAsync(TimeSpan.FromSeconds(5)));
        release.SetResult();Assert.Equal("survived",(await second).Text);
    }
    [Fact]
    public async Task FailedFactory_IsNotCached()
    {
        await using var rig=new Rig();await Assert.ThrowsAsync<InvalidOperationException>(()=>rig.Cache.GetOrCreateAsync<Result>("default:error",_=>throw new InvalidOperationException("business"),rig.Expiry));
        Assert.Equal("recovered",(await rig.Cache.GetOrCreateAsync("default:error",_=>Task.FromResult(new Result("recovered")),rig.Expiry)).Text);
    }
    [Fact]
    public async Task SerializationFailure_ReturnsBusinessValueWithoutSecondCalculation()
    {
        await using var rig=new Rig();var calls=0;
        var value=await rig.Cache.GetOrCreateAsync("default:cycle",_=>{calls++;return Task.FromResult(new Cycle());},rig.Expiry);
        Assert.Same(value,value.Self);Assert.Equal(1,calls);Assert.Null(await rig.Cache.GetAsync<Cycle>("default:cycle"));
    }
    [Fact]
    public async Task Removal_InvalidatesAllValueTypesThroughNativeTags()
    {
        await using var rig=new Rig();await rig.Cache.SetAsync("default:remove",new Result("old"),rig.Expiry);
        await rig.Cache.RemoveAsync("default:remove");
        Assert.Equal("new",(await rig.Cache.GetOrCreateAsync("default:remove",_=>Task.FromResult(new Result("new")),rig.Expiry)).Text);
    }
    [Fact]
    public async Task PayloadLimitAndMemoryBudget_AreBoundedUnderManyKeys()
    {
        var settings=new ApplicationCacheOptions{MemoryLimitBytes=1024*1024,MaximumPayloadBytes=64*1024};await using var rig=new Rig(settings:settings);
        for(var i=0;i<200;i++) await rig.Cache.SetAsync("default:load:"+i,new Result(new string('x',16*1024)),rig.Expiry);
        Assert.InRange(rig.Runtime.Memory.GetCurrentStatistics()!.CurrentEstimatedSize ?? -1,0,settings.MemoryLimitBytes);
        await rig.Cache.SetAsync("default:oversized",new Result(new string('x',128*1024)),rig.Expiry);
        Assert.Null(await rig.Cache.GetAsync<Result>("default:oversized"));
    }
    [Fact]
    public async Task BackendFailure_FallsBackAndBacksOff()
    {
        var backend=new Backend{Fail=true};await using var rig=new Rig();
        using var safe=new FailSafeDistributedCache(backend,rig.Options,rig.Metrics,rig.Time,NullLogger<FailSafeDistributedCache>.Instance);
        Assert.Null(await safe.GetAsync("one"));Assert.Null(await safe.GetAsync("two"));Assert.Equal(1,backend.Reads);
        backend.Fail=false;rig.Time.Advance(TimeSpan.FromSeconds(11));await safe.GetAsync("three");Assert.Equal(2,backend.Reads);
    }
    [Fact]
    public async Task CorruptDistributedPayload_FallsBackWithoutChangingBusinessResult()
    {
        var backend = new Backend();
        var key = "default:corrupt";
        await backend.SetAsync(CacheKeyBuilder.StorageKey<Result>(key,"1"), [0,1,2,3], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
        await using var rig = new Rig(backend);
        var value = await rig.Cache.GetOrCreateAsync(key, _ => Task.FromResult(new Result("calculated")), rig.Expiry);
        Assert.Equal("calculated", value.Text);
    }
    [Fact]
    public async Task DistributedLifetime_ClampsEnvelopeEvenWhenBackendRetainsStaleBytes()
    {
        var backend = new Backend(); await using var rig = new Rig(backend);
        rig.Options.Policies["default"].DistributedLifetime = TimeSpan.FromSeconds(1);
        rig.Options.Policies["default"].MemoryLifetime = TimeSpan.Zero;
        await rig.Cache.SetAsync("default:clamp",new Result("old"),rig.Expiry);
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(await rig.Cache.GetAsync<Result>("default:clamp"));
        Assert.Equal("fresh",(await rig.Cache.GetOrCreateAsync("default:clamp",_=>Task.FromResult(new Result("fresh")),rig.Expiry)).Text);
    }
    [Fact]
    public async Task ExpiryDuringFactory_DoesNotFailOrRecalculateTheRequest()
    {
        await using var rig = new Rig();
        var expiry = rig.Time.GetUtcNow().AddSeconds(1);var calls = 0;
        var result = await rig.Cache.GetOrCreateAsync("panchang:midnight", _ =>
        {
            calls++;rig.Time.Advance(TimeSpan.FromSeconds(2));return Task.FromResult(new Result("original"));
        },expiry);
        Assert.Equal("original",result.Text);Assert.Equal(1,calls);Assert.Null(await rig.Cache.GetAsync<Result>("panchang:midnight"));
    }
    [Fact]
    public async Task NativeTagMetadata_IsBoundedUnderMoreUniqueKeysThanItsBudget()
    {
        var backend = new Backend();await using var rig = new Rig(backend);
        for (var i=0;i<5000;i++) await rig.Cache.SetAsync("default:tags:"+i,new Result("small"),rig.Expiry);
        var field = rig.Runtime.Cache.GetType().GetField("_tagInvalidationTimes",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var tags = (System.Collections.Concurrent.ConcurrentDictionary<string,Task<long>>)field.GetValue(rig.Runtime.Cache)!;
        Assert.InRange(tags.Count,1,4096);
    }
    [Fact]
    public void Keys_AreCanonicalPrivateAndVersioned()
    {
        var a=CacheKeyBuilder.Create("reference",new Dictionary<string,string?>{{"language","en"},{"tradition","telugu"}});
        var b=CacheKeyBuilder.Create("reference",new Dictionary<string,string?>{{"tradition","telugu"},{"language","en"}});
        Assert.Equal(a,b);Assert.DoesNotContain("telugu",a);
        Assert.NotEqual(CacheKeyBuilder.StorageKey<Result>(a,"1"),CacheKeyBuilder.StorageKey<Result>(a,"2"));
        Assert.NotEqual(CacheKeyBuilder.StorageKey<Result>(a,"1"),CacheKeyBuilder.StorageKey<Cycle>(a,"1"));
    }
}
