using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Authorize.Data.DependencyInjection;
using MyVedicStar.Authorize.Data.Persistence;
using Npgsql;

namespace MyVedicStar.Authorize.Tests.Integration.Postgres;

/// <summary>
/// Creates a throwaway PostgreSQL database, applies the real migrations, and drops it afterwards.
/// Set <c>MYVEDICSTAR_TEST_POSTGRES</c> to an admin connection string to enable these tests; otherwise they are skipped.
/// </summary>
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    public const string ConnectionStringVariable = "MYVEDICSTAR_TEST_POSTGRES";

    private readonly string? _adminConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
    private readonly string _databaseName = $"myvedicstar_test_{Guid.NewGuid():N}";
    private ServiceProvider? _services;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_adminConnectionString);

    public IServiceProvider Services => _services ?? throw new InvalidOperationException("PostgreSQL is not configured.");

    /// <summary>Builds a fresh connection from the original credentials, never from an opened connection's sanitized string.</summary>
    public NpgsqlConnection CreateConnection(string? databaseName = null)
    {
        var settings = new NpgsqlConnectionStringBuilder(_adminConnectionString);
        if (databaseName is not null) settings.Database = databaseName;
        return new NpgsqlConnection(settings.ConnectionString);
    }

    public async Task InitializeAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        await ExecuteAdminCommandAsync($"""CREATE DATABASE "{_databaseName}" """);

        var connectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _databaseName }.ConnectionString;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connectionString,
                ["Database:CommandTimeout"] = "00:00:30",
                ["Database:MaxRetryCount"] = "0",
                ["Database:MaxRetryDelay"] = "00:00:00",
                ["Database:ApplyMigrationsOnStartup"] = "false",
            })
            .Build();

        _services = new ServiceCollection()
            .AddLogging()
            .AddAuthorizeData(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthorizeDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        NpgsqlConnection.ClearAllPools();
        await ExecuteAdminCommandAsync($"""DROP DATABASE IF EXISTS "{_databaseName}" WITH (FORCE)""");
    }

    private async Task ExecuteAdminCommandAsync(string sql)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
