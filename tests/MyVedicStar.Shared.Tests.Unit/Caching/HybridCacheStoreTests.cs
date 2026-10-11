using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Shared.Caching;
namespace MyVedicStar.Shared.Tests.Unit.Caching;
public sealed class HybridCacheStoreTests
{
    private sealed record Value(string Text);
    private static ServiceProvider Build(bool enabled=true) => new ServiceCollection().AddMyVedicStarCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Caching:Enabled"]=enabled.ToString() }).Build()).BuildServiceProvider();
    [Fact]
    public async Task SetAndGet_ReturnIndependentValues()
    {
        await using var services=Build();var cache=services.GetRequiredService<ICacheStore>();var value=new Value("safe");
        await cache.SetAsync("reference:star",value,DateTimeOffset.UtcNow.AddHours(1),CancellationToken.None);
        var result=await cache.GetAsync<Value>("reference:star",CancellationToken.None);
        Assert.Equal(value,result);Assert.NotSame(value,result);
    }
    [Fact]
    public async Task Remove_InvalidatesTheNativeEntry()
    {
        await using var services=Build();var cache=services.GetRequiredService<ICacheStore>();
        await cache.SetAsync("reference:remove",new Value("old"),DateTimeOffset.UtcNow.AddHours(1),CancellationToken.None);
        await cache.RemoveAsync("reference:remove",CancellationToken.None);Assert.Null(await cache.GetAsync<Value>("reference:remove",CancellationToken.None));
    }
    [Fact]
    public async Task Disabled_UsesTheFactoryWithoutStoring()
    {
        await using var services=Build(false);var cache=services.GetRequiredService<ICacheStore>();var value=new Value("uncached");
        Assert.Same(value,await cache.GetOrCreateAsync("reference:off",_=>Task.FromResult(value),DateTimeOffset.UtcNow.AddHours(1),CancellationToken.None));
        Assert.Null(await cache.GetAsync<Value>("reference:off",CancellationToken.None));
    }
    [Fact]
    public async Task ExpiredWrite_RemovesTheOldEntry()
    {
        await using var services=Build();var cache=services.GetRequiredService<ICacheStore>();
        await cache.SetAsync("reference:expire",new Value("old"),DateTimeOffset.UtcNow.AddHours(1),CancellationToken.None);
        await cache.SetAsync("reference:expire",new Value("expired"),DateTimeOffset.UtcNow.AddSeconds(-1),CancellationToken.None);
        Assert.Null(await cache.GetAsync<Value>("reference:expire",CancellationToken.None));
    }
}
