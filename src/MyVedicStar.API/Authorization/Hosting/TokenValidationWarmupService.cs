using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace MyVedicStar.API.Hosting;

/// <summary>
/// Fetches each bearer scheme's OpenID metadata and signing keys in the background at startup, so the first
/// authenticated request on a new replica does not wait for them. The handlers' configuration managers cache and
/// refresh the result as usual; failures only log, and the first request then fetches as before.
/// </summary>
internal sealed partial class TokenValidationWarmupService(IAuthenticationSchemeProvider schemes,
    IOptionsMonitor<JwtBearerOptions> bearerOptions, ILogger<TokenValidationWarmupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bearerSchemes = (await schemes.GetAllSchemesAsync()).Where(scheme => typeof(JwtBearerHandler).IsAssignableFrom(scheme.HandlerType));
        await Task.WhenAll(bearerSchemes.Select(scheme => WarmUpAsync(scheme.Name, stoppingToken)));
    }

    private async Task WarmUpAsync(string scheme, CancellationToken stoppingToken)
    {
        if (bearerOptions.Get(scheme).ConfigurationManager is not { } configurationManager) return;
        var started = Stopwatch.GetTimestamp();
        try
        {
            await configurationManager.GetConfigurationAsync(stoppingToken);
            LogWarmedUp(logger, scheme, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogWarmupFailed(logger, scheme, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Token validation metadata for {Scheme} loaded in {DurationMs}ms")]
    private static partial void LogWarmedUp(ILogger logger, string scheme, double durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token validation metadata for {Scheme} could not be preloaded ({ErrorType})")]
    private static partial void LogWarmupFailed(ILogger logger, string scheme, string errorType);
}
