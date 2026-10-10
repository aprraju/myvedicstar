using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyVedicStar.Shared.Caching;

namespace MyVedicStar.Shared.Tests.Unit.Caching;

public sealed class CachingRegistrationTests
{
    [Fact]
    public void AddMyVedicStarCaching_RegistersOneSingletonInMemoryStore()
    {
        using var services = BuildServices([]);

        Assert.IsType<MemoryCacheStore>(services.GetRequiredService<ICacheStore>());
        Assert.Same(services.GetRequiredService<ICacheStore>(), services.GetRequiredService<ICacheStore>());
    }

    [Fact]
    public void AddMyVedicStarCaching_CalledTwice_RegistersStoreOnce()
    {
        var collection = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        collection.AddMyVedicStarCaching(configuration);
        collection.AddMyVedicStarCaching(configuration);

        Assert.Single(collection, descriptor => descriptor.ServiceType == typeof(ICacheStore));
    }

    [Fact]
    public void AddMyVedicStarCaching_BindsSettings()
    {
        using var services = BuildServices(new() { [$"{CachingOptions.SectionName}:MaxEntries"] = "500" });

        Assert.Equal(500, services.GetRequiredService<IOptions<CachingOptions>>().Value.MaxEntries);
    }

    [Theory]
    [InlineData("MaxEntries", "0")]
    [InlineData("CompactionPercentage", "1.5")]
    [InlineData("ExpirationScanFrequency", "00:00:00")]
    public void AddMyVedicStarCaching_InvalidSetting_FailsValidation(string setting, string value)
    {
        using var services = BuildServices(new() { [$"{CachingOptions.SectionName}:{setting}"] = value });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<CachingOptions>>().Value);
        Assert.Contains(setting, exception.Message);
    }

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddMyVedicStarCaching(configuration).BuildServiceProvider();
    }
}
