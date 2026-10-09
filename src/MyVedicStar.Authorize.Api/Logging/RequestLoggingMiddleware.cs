using System.Diagnostics;

namespace MyVedicStar.Authorize.Api.Logging;

/// <summary>
/// Writes one structured log entry per request. Headers, cookies and bodies are never logged.
/// </summary>
internal sealed class RequestLoggingMiddleware
{
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
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {DurationMs} ms. StartedAtUtc={StartedAtUtc} TraceId={TraceId} UserAgent={UserAgent} Authenticated={IsAuthenticated}",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                startedAtUtc,
                Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
                context.Request.Headers.UserAgent.ToString(),
                context.User.Identity?.IsAuthenticated ?? false);
        }
    }
}
