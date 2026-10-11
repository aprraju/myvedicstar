using Microsoft.EntityFrameworkCore;
using MyVedicStar.Authorize.Data.Options;

namespace MyVedicStar.Authorize.Data.Persistence;

/// <summary>Npgsql provider settings shared by the runtime and design-time context factories.</summary>
internal static class AuthorizeDbContextOptionsConfigurator
{
    public static void Configure(DbContextOptionsBuilder builder, DatabaseOptions? options)
    {
        if (options is null)
        {
            // Design time (dotnet ef migrations add) needs the provider but no live connection.
            builder.UseNpgsql(ConfigureNpgsql);
            return;
        }

        builder.UseNpgsql(options.ConnectionString, npgsql =>
        {
            ConfigureNpgsql(npgsql);
            npgsql.CommandTimeout((int)options.CommandTimeout.TotalSeconds);
            npgsql.EnableRetryOnFailure(options.MaxRetryCount, options.MaxRetryDelay, errorCodesToAdd: null);
        });
    }

    private static void ConfigureNpgsql(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql)
    {
        npgsql.MigrationsHistoryTable(AuthorizeDbContext.MigrationsHistoryTable, AuthorizeDbContext.Schema);
        npgsql.ConfigureDataSource(builder => builder.Name = "myvedicstar-business");
    }
}
