using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Api.RateLimiting;

/// <summary>
/// Per-client request limit for API endpoints (health and documentation endpoints are not limited).
/// Clients are identified by IP address after trusted forwarded headers are applied. Each instance counts
/// independently, so the effective limit scales with the number of replicas.
/// </summary>
public sealed class ApiRateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests allowed per client in each <see cref="Window"/>.</summary>
    [Range(1, 100_000)]
    public int PermitLimit { get; set; }

    /// <summary>Length of the fixed counting window.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Window { get; set; }

    /// <summary>Requests queued, rather than rejected, once the limit is reached.</summary>
    [Range(0, 1_000)]
    public int QueueLimit { get; set; }
}
