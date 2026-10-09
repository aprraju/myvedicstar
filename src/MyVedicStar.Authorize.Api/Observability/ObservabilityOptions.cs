using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Api.Observability;

/// <summary>Telemetry settings. The Application Insights connection string is read from <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c>.</summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>Service name reported with all telemetry (the Application Insights cloud role name).</summary>
    [Required]
    public string ServiceName { get; set; } = string.Empty;
}
