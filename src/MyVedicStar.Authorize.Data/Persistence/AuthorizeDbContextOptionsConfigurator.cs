using Microsoft.EntityFrameworkCore;
using MyVedicStar.Authorize.Data.Options;
using Npgsql;

namespace MyVedicStar.Authorize.Data.Persistence;

/// <summary>Npgsql provider settings shared by the runtime and design-time context factories.</summary>
internal static class AuthorizeDbContextOptionsConfigurator
{
    public static void Configure(DbContextOptionsBuilder builder, DatabaseOptions? options, NpgsqlDataSource? dataSource = null)
    {
        if (options is null)
        {
            // Design time (dotnet ef migrations add) needs the provider but no live connection.
            builder.UseNpgsql("Host=localhost;Database=myvedicstar_design_time", ConfigureNpgsql);
            return;
        }

        void ConfigureRuntime(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql)
        {
            ConfigureNpgsql(npgsql);
            npgsql.CommandTimeout((int)options.CommandTimeout.TotalSeconds);
            npgsql.EnableRetryOnFailure(options.MaxRetryCount, options.MaxRetryDelay, errorCodesToAdd: null);
        }
        if (dataSource is not null) builder.UseNpgsql(dataSource, ConfigureRuntime);
        else builder.UseNpgsql(options.ConnectionString, ConfigureRuntime);
    }

    private static void ConfigureNpgsql(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql)
    {
        npgsql.MigrationsHistoryTable(AuthorizeDbContext.MigrationsHistoryTable, AuthorizeDbContext.Schema);
    }
}
