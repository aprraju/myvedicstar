using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Data.Options;
using MyVedicStar.Authorize.Data.Persistence;

namespace MyVedicStar.Infrastructure.Data;

public static class DatabaseConfiguration
{
    /// <summary>Configures another domain context with the existing database pool and credentials.</summary>
    public static void ConfigureDomainDatabase(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder builder,
        IServiceProvider provider, string schema)
    {
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        builder.UseNpgsql(provider.GetRequiredService<AuthorizeDatabaseDataSource>().Source, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema);
            npgsql.CommandTimeout((int)options.CommandTimeout.TotalSeconds);
            npgsql.EnableRetryOnFailure(options.MaxRetryCount, options.MaxRetryDelay, null);
        });
    }

}
