using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MyVedicDay.Caching;

/// <summary>
/// Registers the distributed cache: Redis when a connection string is configured, an in-process memory cache otherwise.
/// </summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IDistributedObjectCache"/> backed by Redis when <c>Redis:ConnectionString</c> is configured,
    /// or by an in-process memory cache otherwise. Registers a readiness health check tagged <c>ready</c>.
    /// Safe to call from several modules: only the first call registers.
    /// </summary>
    public static IServiceCollection AddDistributedObjectCache(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(IDistributedObjectCache)))
        {
            return services;
        }

        services
            .AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var connectionString = configuration[$"{RedisOptions.SectionName}:{nameof(RedisOptions.ConnectionString)}"];

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var instanceName = configuration[$"{RedisOptions.SectionName}:{nameof(RedisOptions.InstanceName)}"]
                               ?? new RedisOptions().InstanceName;

            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = connectionString;
                options.InstanceName  = instanceName;
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.TryAddSingleton<IDistributedObjectCache, DistributedObjectCache>();
        services.AddHealthChecks().AddCheck<DistributedCacheHealthCheck>("distributed-cache", tags: ["ready"]);

        return services;
    }
}
