using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace MyVedicDay.Caching.Tests.Unit;

/// <summary>Redis when a connection string is configured, an in-process cache otherwise; and the readiness probe.</summary>
public sealed class CachingRegistrationTests
{
    [Fact]
    public void WithoutAConnectionString_AnInProcessCacheIsUsed()
    {
        using var provider = Build(new Dictionary<string, string?>());

        Assert.IsType<MemoryDistributedCache>(provider.GetRequiredService<IDistributedCache>());
        Assert.NotNull(provider.GetRequiredService<IDistributedObjectCache>());
    }

    [Fact]
    public void WithAConnectionString_RedisIsUsed_WithTheConfiguredKeyPrefix()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Redis:ConnectionString"] = "localhost:6379,abortConnect=false",
            ["Redis:InstanceName"] = "myvedicday:prod:",
        });

        // Resolving does not connect: the connection is opened on first use.
        Assert.IsAssignableFrom<RedisCache>(provider.GetRequiredService<IDistributedCache>());
        var redis = provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
        Assert.Equal(("localhost:6379,abortConnect=false", "myvedicday:prod:"), (redis.Configuration, redis.InstanceName));
    }

    [Fact]
    public void RegisteringTwice_RegistersOnce()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddLogging();

        services.AddDistributedObjectCache(configuration).AddDistributedObjectCache(configuration);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDistributedObjectCache));
    }

    [Fact]
    public void InvalidKeyPrefix_FailsValidation()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Redis:InstanceName"] = "Has Spaces" });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RedisOptions>>().Value);
    }

    [Fact]
    public async Task HealthCheck_IsHealthyWhenTheCacheRoundTrips_AndDegradedWhenItFails()
    {
        var healthy = new DistributedCacheHealthCheck(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        var failing = new DistributedCacheHealthCheck(new FailingCache(new InvalidOperationException("Redis is down")));

        Assert.Equal(HealthStatus.Healthy, (await healthy.CheckHealthAsync(new HealthCheckContext())).Status);
        Assert.Equal(HealthStatus.Degraded, (await failing.CheckHealthAsync(new HealthCheckContext())).Status);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddLogging().AddDistributedObjectCache(configuration).BuildServiceProvider();
    }
}
