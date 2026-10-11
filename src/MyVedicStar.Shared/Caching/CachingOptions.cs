using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Shared.Caching;

/// <summary>Legacy configuration retained for source compatibility. Active cache limits are set by MyVedicStar.Caching.ApplicationCacheOptions.</summary>
public sealed class CachingOptions
{
    public const string SectionName = "Caching";

    /// <summary>
    /// Maximum number of entries held by each API instance. When reached, new entries are not cached until
    /// compaction frees space, which bounds memory use.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long MaxEntries { get; set; } = 10_000;

    /// <summary>Fraction of entries removed (least recently used first) when <see cref="MaxEntries"/> is exceeded.</summary>
    [Range(0.01, 0.99)]
    public double CompactionPercentage { get; set; } = 0.25;

    /// <summary>How often expired entries are swept from memory.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan ExpirationScanFrequency { get; set; } = TimeSpan.FromMinutes(1);
}
