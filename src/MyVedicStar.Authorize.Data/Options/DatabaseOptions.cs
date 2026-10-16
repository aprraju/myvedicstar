using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Data.Options;

/// <summary>PostgreSQL settings. All values are supplied by configuration and validated at startup.</summary>
public sealed class DatabaseOptions : IValidatableObject
{
    public const string SectionName = "Database";

    /// <summary>Npgsql connection string. Keep credentials in user secrets or a vault, never in source control.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    public bool UseManagedIdentity { get; set; }
    public string? Host { get; set; }
    public string? Name { get; set; }
    public string? Username { get; set; }
    [Range(1, 65535)] public int Port { get; set; } = 5432;
    public Npgsql.SslMode SslMode { get; set; } = Npgsql.SslMode.VerifyFull;
    public string? ManagedIdentityClientId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!UseManagedIdentity)
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
                yield return new ValidationResult("Database:ConnectionString is required when managed identity is disabled.", [nameof(ConnectionString)]);
            yield break;
        }
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Username))
            yield return new ValidationResult("Managed identity requires Database:Host, Name and Username.", [nameof(Host), nameof(Name), nameof(Username)]);
        if (SslMode is not (Npgsql.SslMode.Require or Npgsql.SslMode.VerifyCA or Npgsql.SslMode.VerifyFull))
            yield return new ValidationResult("Managed identity requires TLS: SslMode must be Require, VerifyCA or VerifyFull.", [nameof(SslMode)]);
        if (ManagedIdentityClientId is not null && !Guid.TryParse(ManagedIdentityClientId, out _))
            yield return new ValidationResult("ManagedIdentityClientId must be a UUID.", [nameof(ManagedIdentityClientId)]);
    }

    internal string BuildConnectionString() => !UseManagedIdentity ? ConnectionString :
        new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = Host, Database = Name, Username = Username, Port = Port, SslMode = SslMode,
            // Azure Database for PostgreSQL has no GSSAPI; skip probing for it (and the missing native library) on every connection.
            GssEncryptionMode = Npgsql.GssEncryptionMode.Disable,
        }.ConnectionString;

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
