using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Caching;

namespace MyVedicDay.Caching;

public static class CachingServiceCollectionExtensions
{
    /// <summary>Registers the common native HybridCache and a compatibility adapter for existing Day cache callers.</summary>
    public static IServiceCollection AddDistributedObjectCache(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApplicationCaching(configuration);
        if (services.Any(entry => entry.ServiceType == typeof(IDistributedObjectCache))) return services;
        services.TryAddSingleton<IDistributedObjectCache, HybridObjectCache>();
        services.AddHealthChecks().AddCheck<DistributedCacheHealthCheck>("distributed-cache", tags: ["ready"]);
        return services;
    }
}
