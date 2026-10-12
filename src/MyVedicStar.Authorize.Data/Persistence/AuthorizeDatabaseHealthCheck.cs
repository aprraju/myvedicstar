using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace MyVedicStar.Authorize.Data.Persistence;

/// <summary>Opens a connection explicitly so authentication failures retain their SQLSTATE.</summary>
internal sealed class AuthorizeDatabaseHealthCheck(AuthorizeDatabaseDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await dataSource.Source.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PostgresException exception)
        {
            // Do not log server Detail or connection strings, which may contain sensitive data.
            return HealthCheckResult.Unhealthy($"PostgreSQL connection failed (SQLSTATE {exception.SqlState}).");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy($"PostgreSQL connection failed ({exception.GetType().Name}).");
        }
    }
}
