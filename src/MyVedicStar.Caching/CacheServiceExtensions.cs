using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace MyVedicStar.Caching;

public static class CacheServiceExtensions
{
    public static IServiceCollection AddApplicationCaching(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(entry => entry.ServiceType == typeof(IApplicationCache))) return services;
        services.AddLogging();
        services.AddMetrics();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<ApplicationCacheOptions>().Bind(configuration.GetSection("Caching")).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ApplicationCacheOptions>, CacheOptionsValidator>());
        services.TryAddSingleton<ApplicationCacheMetrics>();
        var settings = configuration.GetSection("Caching").Get<ApplicationCacheOptions>() ?? new();
        if (settings.Enabled && settings.L2.Enabled)
        {
            var connection = configuration["Database:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("PostgreSQL cache requires Database:ConnectionString from secure runtime configuration.");
            services.AddSingleton<CacheDataSource>(_ => new CacheDataSource(connection, settings));
            services.AddSingleton<PostgresCache>(sp => new PostgresCache(Options.Create(new PostgresCacheOptions
            {
                DataSource = sp.GetRequiredService<CacheDataSource>().Source,
                SchemaName = settings.L2.Schema,
                TableName = settings.L2.Table,
                CreateIfNotExists = false,
                UseWAL = true,
                ExpiredItemsDeletionInterval = settings.L2.CleanupInterval,
                TimeProvider = sp.GetRequiredService<TimeProvider>(),
            })));
            services.AddSingleton<IDistributedCache>(sp => new FailSafeDistributedCache(sp.GetRequiredService<PostgresCache>(), sp.GetRequiredService<IOptions<ApplicationCacheOptions>>().Value, sp.GetRequiredService<ApplicationCacheMetrics>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<FailSafeDistributedCache>>()));
        }
        services.TryAddSingleton<HybridCacheRuntime>(sp => new HybridCacheRuntime(sp.GetRequiredService<IOptions<ApplicationCacheOptions>>().Value, sp.GetRequiredService<ApplicationCacheMetrics>(), sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>(), sp.GetService<IDistributedCache>()));
        services.TryAddSingleton<HybridCache>(sp => sp.GetRequiredService<HybridCacheRuntime>().Cache);
        services.TryAddSingleton<IApplicationCache, HybridApplicationCache>();
        return services;
    }

    private sealed class CacheDataSource : IAsyncDisposable, IDisposable
    {
        internal NpgsqlDataSource Source { get; }
        public CacheDataSource(string connectionString, ApplicationCacheOptions options)
        {
            var connection = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Pooling = true, MinPoolSize = 0, MaxPoolSize = options.L2.MaximumConnections,
                Timeout = options.L2.CommandTimeoutSeconds, CommandTimeout = options.L2.CommandTimeoutSeconds,
                ApplicationName = "myvedicstar-cache",
                Enlist = false, Timezone = "UTC",
            };
            Source = new NpgsqlDataSourceBuilder(connection.ConnectionString) { Name = "myvedicstar-cache" }.Build();
        }
        public void Dispose() => Source.Dispose();
        public ValueTask DisposeAsync() => Source.DisposeAsync();
    }
}
