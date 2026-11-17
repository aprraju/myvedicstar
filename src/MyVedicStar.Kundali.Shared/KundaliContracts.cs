namespace MyVedicStar.Kundali.Shared;

/// <summary>Lifecycle of a generation. A partial result is never `Succeeded`: sections fill in as the engine
/// completes them, and the revision only becomes current when the engine reports complete.</summary>
public enum KundaliGenerationStatus { Pending, Running, Succeeded, Failed }

/// <summary>Stable, safe reason codes for a failure. The API returns the code, never an engine message, so no
/// internal detail or personal data leaks through an error body.</summary>
public static class KundaliFailureReasons
{
    public const string EngineError = "engine_error";
    public const string EphemerisUnavailable = "ephemeris_unavailable";
    public const string TimezoneUnresolvable = "timezone_unresolvable";
    public const string UnsupportedCapability = "unsupported_capability";
    public const string BirthDateOutOfRange = "birth_date_out_of_range";
    public const string Unknown = "unknown";
}

/// <summary>Section names that are stored as independent JSONB columns, so one section can be regenerated without
/// rewriting the whole result (KUN-04 Architect refinement).</summary>
public static class KundaliSections
{
    public const string Overview = "overview";
    public const string Charts = "charts";
    public const string Planets = "planets";
    public const string Dashas = "dashas";
    public const string Yogas = "yogas";
    public const string Ashtakavarga = "ashtakavarga";
    public const string Bala = "bala";
    public const string Transits = "transits";
    public const string Interpretations = "interpretations";

    public static readonly IReadOnlyList<string> All =
        [Overview, Charts, Planets, Dashas, Yogas, Ashtakavarga, Bala, Transits, Interpretations];
}

/// <summary>
/// Provenance stamped on every persisted result. A result is only serviceable when all of these still match the
/// current snapshot and effective settings; otherwise it is stale and never served as current.
/// </summary>
public sealed record KundaliProvenance(
    string EngineVersion,
    string EphemerisVersion,
    string RuleSetVersion,
    string TimezoneDataVersion,
    string Ayanamsa);

/// <summary>Immutable birth snapshot taken from the owned Vedic Profile at generation time. Deliberately a copy:
/// later profile edits must not retroactively change a stored result, they invalidate it (KUN-22).</summary>
public sealed record KundaliInputSnapshot(
    DateOnly DateOfBirth,
    BirthTimeMode BirthTimeMode,
    TimeOnly? BirthTime,
    TimeOnly? BirthTimeFrom,
    TimeOnly? BirthTimeTo,
    decimal Latitude,
    decimal Longitude,
    string TimezoneId,
    long ProfileRevision)
{
    /// <summary>Stable hash identifying this exact input. Part of the idempotency key, so a repeated click cannot
    /// enqueue a second generation for identical inputs.</summary>
    public string ComputeHash()
    {
        var times = string.Join('|', BirthTime?.ToString("HH:mm:ss"), BirthTimeFrom?.ToString("HH:mm:ss"),
            BirthTimeTo?.ToString("HH:mm:ss"));
        var canonical = string.Join('|', DateOfBirth.ToString("yyyy-MM-dd"), BirthTimeMode, times,
            Latitude.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
            Longitude.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
            TimezoneId, ProfileRevision);
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>Current revision summary returned with the aggregate. Section payloads are added by later stories;
/// this foundation stores and versions the envelope.</summary>
public sealed record KundaliRevisionSummary(
    Guid RevisionId,
    long Revision,
    KundaliGenerationStatus Status,
    string? FailureReasonCode,
    KundaliProvenance Provenance,
    string InputSnapshotHash,
    bool IsStale,
    bool IsPartial,
    IReadOnlyList<string> CompletedSections,
    DateTimeOffset CreatedAtUtc);

/// <summary>One aggregate per owned profile. Carries the active revision pointer and the display preferences that
/// must survive a reload without recalculating.</summary>
public sealed record KundaliAggregate(
    Guid Id,
    Guid ProfileId,
    Guid? ActiveRevisionId,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Display preferences are presentation-only (chart style never triggers recalculation), but they are
/// persisted per profile so a returning user sees the same view.</summary>
public sealed record KundaliPreferences(
    string ChartStyle,
    long Revision,
    DateTimeOffset UpdatedAtUtc);

/// <summary>What a read returns: the aggregate plus its current revision summary, or a status when no revision has
/// completed yet.</summary>
public sealed record KundaliView(
    KundaliAggregate Aggregate,
    KundaliRevisionSummary? CurrentRevision);

public static class KundaliLimits
{
    public const int ChartStyle = 32;
    /// <summary>Generous bound so a section payload cannot become an unbounded write.</summary>
    public const int SectionPayloadBytes = 1_048_576;
}
