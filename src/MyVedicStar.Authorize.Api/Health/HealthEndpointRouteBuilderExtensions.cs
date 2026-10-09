using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MyVedicStar.Shared.Health;

namespace MyVedicStar.Authorize.Api.Health;

internal static class HealthEndpointRouteBuilderExtensions
{
    /// <summary>Path shared by all health endpoints; excluded from request logging and tracing.</summary>
    public const string BasePath = "/health";

    /// <summary>Process is running and able to serve HTTP. Runs no dependency checks.</summary>
    public const string LivenessPath = BasePath + "/live";

    /// <summary>Instance can serve traffic: every check tagged <see cref="HealthCheckTags.Readiness"/> is healthy.</summary>
    public const string ReadinessPath = BasePath + "/ready";

    /// <summary>
    /// Maps anonymous liveness and readiness endpoints for container probes. Responses contain only the overall status.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        endpoints.MapHealthChecks(ReadinessPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthCheckTags.Readiness) })
            .AllowAnonymous();

        return endpoints;
    }

    public static bool IsHealthRequest(this HttpContext context) => context.Request.Path.StartsWithSegments(BasePath);
}
