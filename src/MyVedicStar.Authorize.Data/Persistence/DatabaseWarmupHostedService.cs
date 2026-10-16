using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MyVedicStar.Authorize.Data.Persistence;

/// <summary>
/// Pays the one-off database costs of a new replica in the background instead of in the first sign-in: acquiring the
/// managed-identity token, opening a TLS connection into the pool, building the EF model and compiling the query
/// pipeline. Runs alongside startup, never delays it, and failures only log (the readiness check reports real outages).
/// </summary>
internal sealed partial class DatabaseWarmupHostedService(IServiceScopeFactory scopeFactory, ILogger<DatabaseWarmupHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AuthorizeDbContext>();
            await dbContext.Users.AsNoTracking()
                .Where(user => user.Issuer == string.Empty && user.ExternalId == string.Empty)
                .Select(user => user.Id)
                .FirstOrDefaultAsync(stoppingToken);
            var duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            LogWarmedUp(logger, duration);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            var duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            LogWarmupFailed(logger, exception.GetType().Name, duration);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database warm-up completed in {DurationMs}ms")]
    private static partial void LogWarmedUp(ILogger logger, double durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database warm-up failed after {DurationMs}ms ({ErrorType}); the first request will connect instead")]
    private static partial void LogWarmupFailed(ILogger logger, string errorType, double durationMs);
}
