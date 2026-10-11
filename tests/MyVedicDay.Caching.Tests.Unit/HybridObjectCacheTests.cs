using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace MyVedicDay.Caching.Tests.Unit;
public sealed class HybridObjectCacheTests
{
    private sealed record Value(string Text);
    [Fact]
    public async Task Adapter_RoundTripsAndReusesTheNativeFactoryResult()
    {
        await using var services = new ServiceCollection().AddDistributedObjectCache(new ConfigurationBuilder().Build()).BuildServiceProvider();
        var cache=services.GetRequiredService<IDistributedObjectCache>();var calls=0;
        var first=await cache.GetOrCreateAsync("panchang:adapter",_=>{calls++;return Task.FromResult(new Value("same"));},DateTimeOffset.UtcNow.AddHours(1),CancellationToken.None);
        var second=await cache.GetOrCreateAsync("panchang:adapter",_=>{calls++;return Task.FromResult(new Value("different"));},DateTimeOffset.UtcNow.AddHours(1),CancellationToken.None);
        Assert.Equal(1,calls);Assert.Equal(first,second);Assert.NotSame(first,second);
    }
    [Fact]
    public async Task CancelledRequest_PropagatesCancellation()
    {
        await using var services = new ServiceCollection().AddDistributedObjectCache(new ConfigurationBuilder().Build()).BuildServiceProvider();
        var cache=services.GetRequiredService<IDistributedObjectCache>();using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>cache.GetAsync<Value>("panchang:cancel",cancelled.Token));
    }
}
