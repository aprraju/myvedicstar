using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.API.Authorization.Health;

/// <summary>Paths of the anonymous health endpoints used by container probes.</summary>
public sealed class HealthEndpointOptions
{
    public const string SectionName = "HealthChecks";

    [Required, RegularExpression("^/.+")]
    public string LivenessPath { get; set; } = string.Empty;

    [Required, RegularExpression("^/.+")]
    public string ReadinessPath { get; set; } = string.Empty;
}
