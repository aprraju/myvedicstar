using Azure.Core;
using Azure.Identity;
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
            var database = CacheDatabaseSettings.From(configuration);
            services.AddSingleton<CacheDataSource>(_ => new CacheDataSource(database, settings));
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

    /// <summary>
    /// The business database's sign-in, read from the same <c>Database</c> section: a connection string, or (Azure) managed
    /// identity with Host, Name and Username and an Entra access token as the password.
    /// </summary>
    internal sealed record CacheDatabaseSettings(string ConnectionString, bool UseManagedIdentity, string? ManagedIdentityClientId)
    {
        internal static CacheDatabaseSettings From(IConfiguration configuration)
        {
            var database = configuration.GetSection("Database");
            // Like the business data source: the container's user-assigned identity (AZURE_CLIENT_ID) unless configured.
            var clientId = database["ManagedIdentityClientId"] ?? configuration["AZURE_CLIENT_ID"];
            if (!database.GetValue<bool>("UseManagedIdentity"))
            {
                var connection = database["ConnectionString"];
                if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("PostgreSQL cache requires Database:ConnectionString from secure runtime configuration, or Database:UseManagedIdentity.");
                return new(connection, false, null);
            }
            var (host, name, username) = (database["Host"], database["Name"], database["Username"]);
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(username))
                throw new InvalidOperationException("PostgreSQL cache with managed identity requires Database:Host, Name and Username.");
            var sslMode = database.GetValue("SslMode", SslMode.VerifyFull);
            if (sslMode is not (SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull))
                throw new InvalidOperationException("PostgreSQL cache with managed identity requires TLS: Database:SslMode must be Require, VerifyCA or VerifyFull.");
            var connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = host, Database = name, Username = username, Port = database.GetValue("Port", 5432), SslMode = sslMode,
                // Azure Database for PostgreSQL has no GSSAPI; skip probing for it on every connection.
                GssEncryptionMode = GssEncryptionMode.Disable,
            }.ConnectionString;
            return new(connectionString, true, string.IsNullOrWhiteSpace(clientId) ? null : clientId);
        }
    }

    private sealed class CacheDataSource : IAsyncDisposable, IDisposable
    {
        private static readonly TokenRequestContext PostgreSqlTokenRequest = new(["https://ossrdbms-aad.database.windows.net/.default"]);
        internal NpgsqlDataSource Source { get; }
        public CacheDataSource(CacheDatabaseSettings database, ApplicationCacheOptions options, TokenCredential? credential = null)
        {
            var connection = new NpgsqlConnectionStringBuilder(database.ConnectionString)
            {
                Pooling = true, MinPoolSize = 0, MaxPoolSize = options.L2.MaximumConnections,
                Timeout = options.L2.CommandTimeoutSeconds, CommandTimeout = options.L2.CommandTimeoutSeconds,
                ApplicationName = "myvedicstar-cache",
                Enlist = false, Timezone = "UTC",
            };
            var builder = new NpgsqlDataSourceBuilder(connection.ConnectionString) { Name = "myvedicstar-cache" };
            if (database.UseManagedIdentity)
            {
                // Same identity and token scope as the business data source; the SDK caches and refreshes the token.
                var identity = credential ?? (database.ManagedIdentityClientId is { } clientId
                    ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
                    : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
                builder.UsePasswordProvider(
                    _ => identity.GetToken(PostgreSqlTokenRequest, CancellationToken.None).Token,
                    async (_, cancellationToken) => (await identity.GetTokenAsync(PostgreSqlTokenRequest, cancellationToken)).Token);
            }
            Source = builder.Build();
        }
        public void Dispose() => Source.Dispose();
        public ValueTask DisposeAsync() => Source.DisposeAsync();
    }
}
