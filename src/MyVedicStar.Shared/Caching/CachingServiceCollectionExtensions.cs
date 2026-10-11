using MyVedicStar.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MyVedicStar.Shared.Caching;

public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICacheStore"/> as a native HybridCache adapter (settings: <c>Caching</c>).
    /// Safe to call from several domains; the cache is registered once.
    /// </summary>
    public static IServiceCollection AddMyVedicStarCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CachingOptions>()
            .Bind(configuration.GetSection(CachingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddApplicationCaching(configuration);
        services.AddMetrics();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<CacheMetrics>();
        services.TryAddSingleton<ICacheStore, HybridCacheStore>();

        return services;
    }
}
