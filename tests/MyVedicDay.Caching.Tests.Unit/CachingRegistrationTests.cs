using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyVedicStar.Caching;
namespace MyVedicDay.Caching.Tests.Unit;
public sealed class CachingRegistrationTests
{
    [Fact]
    public async Task DefaultRegistration_UsesNativeHybridWithoutRedis()
    {
        await using var provider = new ServiceCollection().AddLogging().AddDistributedObjectCache(new ConfigurationBuilder().Build()).BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IApplicationCache>());
        Assert.IsType<HybridObjectCache>(provider.GetRequiredService<IDistributedObjectCache>());
    }
    [Fact]
    public void RegisteringTwice_RegistersOnce()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddLogging();
        services.AddDistributedObjectCache(configuration).AddDistributedObjectCache(configuration);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDistributedObjectCache));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IApplicationCache));
    }
    [Fact]
    public async Task InvalidMemoryBudget_FailsValidation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Caching:MemoryLimitBytes"] = "10" }).Build();
        await using var provider = new ServiceCollection().AddDistributedObjectCache(configuration).BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ApplicationCacheOptions>>().Value);
    }
}
