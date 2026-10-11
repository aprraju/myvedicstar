using System.Diagnostics;

namespace MyVedicStar.Authorize.Api.Logging;

/// <summary>
/// Writes one structured log entry per request. Headers (other than a shortened User-Agent), cookies and bodies are
/// never logged.
/// </summary>
internal sealed partial class RequestLoggingMiddleware
{
    // The User-Agent is caller-controlled; capping it stops one request from writing an arbitrarily large log entry.
    private const int MaxUserAgentLength = 256;

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await _next(context);
        }
        finally
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                // CA1873 does not recognise the IsEnabled guard above, so it reports these arguments as always evaluated.
#pragma warning disable CA1873
                LogRequestCompleted(
                    _logger,
                    context.Request.Method,
                    context.Request.Path.Value,
                    context.Response.StatusCode,
                    (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                    startedAtUtc,
                    Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
                    GetUserAgent(context.Request),
                    context.User.Identity?.IsAuthenticated ?? false);
#pragma warning restore CA1873
            }
        }
    }

    private static string GetUserAgent(HttpRequest request)
    {
        var userAgent = request.Headers.UserAgent.ToString();
        return userAgent.Length <= MaxUserAgentLength ? userAgent : userAgent[..MaxUserAgentLength];
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "HTTP {Method} {Path} responded {StatusCode} in {DurationMs} ms. StartedAtUtc={StartedAtUtc} TraceId={TraceId} UserAgent={UserAgent} Authenticated={IsAuthenticated}")]
    private static partial void LogRequestCompleted(
        ILogger logger,
        string method,
        string? path,
        int statusCode,
        long durationMs,
        DateTimeOffset startedAtUtc,
        string traceId,
        string userAgent,
        bool isAuthenticated);
}
