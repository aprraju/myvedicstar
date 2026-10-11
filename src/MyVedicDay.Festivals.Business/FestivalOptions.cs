using System.ComponentModel.DataAnnotations;

namespace MyVedicDay.Festivals.Business;

/// <summary>
/// Festival settings. Bound from the <c>Festivals</c> configuration section and validated at startup.
/// </summary>
public sealed class FestivalOptions
{
    public const string SectionName = "Festivals";

    /// <summary>Largest number of days an upcoming-festivals request may cover.</summary>
    [Range(1, 366)]
    public int MaxUpcomingDays { get; set; } = 90;

    /// <summary>Largest number of days a single range request may cover (a year view).</summary>
    [Range(1, 400)]
    public int MaxRangeDays { get; set; } = 366;

    /// <summary>Cache lifetime for dates that have already ended at the location.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan PastDateCacheLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Upper bound on any cache entry's lifetime.</summary>
    [Range(typeof(TimeSpan), "01:00:00", "400.00:00:00")]
    public TimeSpan MaxCacheLifetime { get; set; } = TimeSpan.FromDays(35);
}
