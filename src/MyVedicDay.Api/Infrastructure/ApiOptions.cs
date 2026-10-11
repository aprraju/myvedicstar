using System.ComponentModel.DataAnnotations;

namespace MyVedicDay.Api.Infrastructure;

/// <summary>Browser origins allowed to call the API. Bound from <c>Cors</c>.</summary>
public sealed class ApiCorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>OpenAPI document and Swagger UI exposure. Bound from <c>ApiDocumentation</c>.</summary>
public sealed class ApiDocumentationOptions
{
    public const string SectionName = "ApiDocumentation";

    /// <summary>
    /// Expose <c>/openapi/v1.json</c> and <c>/swagger</c> outside Development (default: on in every environment). Always
    /// on in Development; set <c>ApiDocumentation__Enabled=false</c> to hide them in an environment.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// HTTP paths for the liveness and readiness health endpoints.
/// Bound from <c>HealthChecks</c>; values here are the single source of truth — the same paths
/// are used by <see cref="ApiApplicationBuilderExtensions"/> to register the endpoints and by
/// <c>deployment/update.ps1</c> to configure Container App health probes.
/// </summary>
internal sealed class HealthCheckEndpointOptions
{
    public const string SectionName = "HealthChecks";

    public string LivenessPath  { get; init; } = "/health/live";
    public string ReadinessPath { get; init; } = "/health/ready";
}

/// <summary>OpenTelemetry export settings. Bound from <c>Observability</c>.</summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    [Required]
    public string ServiceName { get; set; } = "myvedicday-api";

    /// <summary>OTLP collector endpoint. Telemetry is collected but not exported when empty.</summary>
    public Uri? OtlpEndpoint { get; set; }
}
