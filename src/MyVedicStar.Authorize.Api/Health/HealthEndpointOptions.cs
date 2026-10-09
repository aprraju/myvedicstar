using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Api.Health;

/// <summary>Paths of the anonymous health endpoints used by container probes.</summary>
public sealed class HealthEndpointOptions
{
    public const string SectionName = "HealthChecks";

    /// <summary>Process is running and able to serve HTTP; runs no dependency checks. For example <c>/health/live</c>.</summary>
    [Required]
    [RegularExpression("^/.+")]
    public string LivenessPath { get; set; } = string.Empty;

    /// <summary>Instance can serve traffic: every readiness-tagged check is healthy. For example <c>/health/ready</c>.</summary>
    [Required]
    [RegularExpression("^/.+")]
    public string ReadinessPath { get; set; } = string.Empty;

    /// <summary>Whether a request targets a health endpoint (excluded from request logging and tracing).</summary>
    public bool IsHealthRequest(PathString path) =>
        path.StartsWithSegments(LivenessPath) || path.StartsWithSegments(ReadinessPath);
}
