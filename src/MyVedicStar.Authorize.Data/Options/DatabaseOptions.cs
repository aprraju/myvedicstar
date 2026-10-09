using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Data.Options;

/// <summary>PostgreSQL settings. All values are supplied by configuration and validated at startup.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Npgsql connection string. Keep credentials in user secrets or a vault, never in source control.</summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Maximum time a single database command may run.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan CommandTimeout { get; set; }

    /// <summary>How many times a transient database failure is retried.</summary>
    [Range(0, 10)]
    public int MaxRetryCount { get; set; }

    /// <summary>Longest delay between retries.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:01:00")]
    public TimeSpan MaxRetryDelay { get; set; }

    /// <summary>
    /// Apply pending EF Core migrations when the application starts. Intended for local development only;
    /// production deployments should apply migrations as a separate, controlled step.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}
