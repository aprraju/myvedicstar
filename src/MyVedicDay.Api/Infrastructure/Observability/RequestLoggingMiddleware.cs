using System.Diagnostics;
using System.Reflection;

namespace MyVedicDay.Api.Infrastructure.Observability;

/// <summary>
/// Writes one structured log entry per request. The query string is deliberately not logged because it carries
/// precise coordinates; the Business layer logs the resolved time zone instead.
/// </summary>
internal sealed partial class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    private const int MaxUserAgentLength = 256;

    private static readonly string ApplicationVersion =
        typeof(RequestLoggingMiddleware).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(httpContext);
        }
        finally
        {
            var request = httpContext.Request;
            var statusCode = httpContext.Response.StatusCode;
            var userAgent = request.Headers.UserAgent.ToString();
            if (userAgent.Length > MaxUserAgentLength)
            {
                userAgent = userAgent[..MaxUserAgentLength];
            }

            LogRequestCompleted(
                statusCode >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Information,
                request.Method,
                request.Path.Value ?? string.Empty,
                statusCode,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                startedAt,
                userAgent,
                ApplicationVersion);
        }
    }

    [LoggerMessage(Message = "HTTP {HttpMethod} {Path} responded {StatusCode} in {DurationMs:0.0} ms (started {RequestStartedUtc:o}, user agent \"{UserAgent}\", version {ApplicationVersion})")]
    private partial void LogRequestCompleted(
        LogLevel level, string httpMethod, string path, int statusCode, double durationMs, DateTimeOffset requestStartedUtc, string userAgent, string applicationVersion);
}
