using System.ComponentModel.DataAnnotations;

namespace MyVedicDay.Caching;

/// <summary>
/// Distributed cache settings. Bound from the <c>Redis</c> configuration section.
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// StackExchange.Redis connection string. When empty, an in-process cache is used, which is suitable
    /// only for local development and single-instance deployments. Supply it from a secret store, never source control.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Prefix applied to every key so multiple applications can share a Redis instance.</summary>
    [Required]
    [RegularExpression("^[a-z0-9:_-]{1,32}$")]
    public string InstanceName { get; set; } = "myvedicday:";
}
