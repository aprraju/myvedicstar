using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Api.Networking;

/// <summary>
/// Which reverse proxies / load balancers may supply <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c>.
/// Headers from any other source are ignored. All values are supplied by configuration and validated at startup.
/// </summary>
public sealed class TrustedProxyOptions
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Maximum number of proxy hops to unwind from <c>X-Forwarded-For</c>.</summary>
    [Range(1, 10)]
    public int ForwardLimit { get; set; }

    /// <summary>IP addresses of trusted proxies, for example <c>10.0.0.4</c>.</summary>
    [Required]
    public string[] KnownProxies { get; set; } = [];

    /// <summary>CIDR ranges of trusted proxies, for example <c>10.0.0.0/24</c>.</summary>
    [Required]
    public string[] KnownNetworks { get; set; } = [];
}
