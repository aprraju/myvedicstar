using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Data.Options;

namespace MyVedicStar.Authorize.Data.Persistence;

/// <summary>
/// Applies pending migrations at startup when <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/> is enabled.
/// </summary>
internal sealed class DatabaseMigrationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DatabaseOptions _options;
    private readonly ILogger<DatabaseMigrationHostedService> _logger;

    public DatabaseMigrationHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<DatabaseOptions> options,
        ILogger<DatabaseMigrationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.ApplyMigrationsOnStartup)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthorizeDbContext>();

        _logger.LogInformation("Applying database migrations");
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
