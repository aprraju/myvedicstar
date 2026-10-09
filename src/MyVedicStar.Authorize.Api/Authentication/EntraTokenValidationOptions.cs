using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Api.Authentication;

/// <summary>
/// Extra validation applied to the Entra External ID bearer scheme registered by MyVedicStar.ApiProtection.
/// Bound from <c>Authentication:EntraExternalId</c>, next to the package's own TenantDomain / TenantId / ClientId.
/// </summary>
public sealed class EntraTokenValidationOptions
{
    public const string SectionName = "Authentication:EntraExternalId";

    /// <summary>Signing algorithms accepted for incoming tokens, for example <c>RS256</c>.</summary>
    [Required]
    [MinLength(1)]
    public string[] ValidAlgorithms { get; set; } = [];

    /// <summary>Allowed clock difference when validating token lifetime.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:05:00")]
    public TimeSpan ClockSkew { get; set; }

    /// <summary>How often the cached metadata and signing keys are refreshed automatically.</summary>
    [Range(typeof(TimeSpan), "00:05:00", "7.00:00:00")]
    public TimeSpan MetadataAutomaticRefreshInterval { get; set; }

    /// <summary>Minimum time between forced metadata refreshes (for example after an unknown signing key).</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan MetadataRefreshInterval { get; set; }

    /// <summary>Timeout for retrieving the metadata document and signing keys.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan MetadataTimeout { get; set; }
}
