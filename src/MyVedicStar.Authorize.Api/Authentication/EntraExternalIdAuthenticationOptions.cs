using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Api.Authentication;

/// <summary>
/// Settings for authenticating bearer tokens issued by a Microsoft Entra External ID tenant.
/// All values are supplied by configuration and validated at startup.
/// </summary>
public sealed class EntraExternalIdAuthenticationOptions
{
    public const string SectionName = "Authentication:EntraExternalId";

    /// <summary>
    /// OpenID Connect metadata document, for example
    /// <c>https://{subdomain}.ciamlogin.com/{tenantId}/v2.0/.well-known/openid-configuration</c>.
    /// The token issuer and signing keys are read from it.
    /// </summary>
    [Required]
    [Url]
    public string MetadataAddress { get; set; } = string.Empty;

    /// <summary>Whether the metadata document must be served over HTTPS.</summary>
    public bool RequireHttpsMetadata { get; set; }

    /// <summary>Expected token audience: the application (client) ID of this API's app registration.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

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
