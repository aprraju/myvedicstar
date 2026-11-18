using MyVedicStar.VedicProfiles.Shared;

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
    /// <summary>True only when the profile supplied a single exact birth time. Downstream sections read this one
    /// property to decide whether time-dependent output is computable, instead of pattern-matching the enum and
    /// risking a new mode being silently treated as exact. Derived from <see cref="BirthTimeMode"/> - deliberately
    /// not stored, so the two cannot disagree.</summary>
    public bool HasExactBirthTime => BirthTimeMode == BirthTimeMode.Known && BirthTime.HasValue;

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

/// <summary>
/// Why an overview field could not be produced. Carried per unsupported entry so the UI can tell a missing engine
/// apart from genuine birth-time uncertainty; rendering the two identically would present"not built yet" as
/// "we cannot know", which is a different and much worse claim.
/// </summary>
public static class KundaliUnsupportedReasons
{
    /// <summary>The calculation engine (#60) is not wired into this module yet.</summary>
    public const string EngineUnavailable = "engine-unavailable";

    /// <summary>The birth time is unknown, so time-dependent values are genuinely indeterminate.</summary>
    public const string BirthTimeUnknown = "birth-time-unknown";

    /// <summary>The birth time is a range, so time-dependent values cannot be pinned to one instant.</summary>
    public const string BirthTimeApproximate = "birth-time-approximate";

    /// <summary>The engine withheld a time-dependent value because the birth time is not exact. Emitted by the
    /// engine itself, carried through unchanged so the API vocabulary matches the calculation vocabulary.</summary>
    public const string RequiresExactBirthTime = "requires-exact-birth-time";

    /// <summary>Computable, but no independent reference exists to validate it against. Distinct from
    /// both "not built" and "cannot know": this is a validation task, and the UI must be able to say
    /// "computed but not yet proven" rather than "unavailable".</summary>
    public const string UnvalidatedReference = "unvalidated-reference";
}

/// <summary>One field that could not be computed, with a reason code. The path is the response property name so a
/// client can bind it directly to the field it chose not to render.</summary>
public sealed record KundaliUnsupportedField(string Path, string Reason);

/// <summary>Birth inputs shown back to the user, taken from the owned profile. Time fields are absent rather than
/// sentinel-valued: an unknown birth time omits them entirely so no consumer can mistake midnight for a real time.</summary>
public sealed record KundaliBirthDetails(
    string? Name,
    DateOnly BirthDate,
    KundaliBirthTimeMode BirthTimeMode,
    TimeOnly? BirthTime,
    TimeOnly? BirthTimeFrom,
    TimeOnly? BirthTimeTo,
    string PlaceName,
    decimal Latitude,
    decimal Longitude,
    string TimezoneId);

/// <summary>Wire-level birth time mode. Distinct from the profile's <c>BirthTimeMode</c> so the API contract can
/// evolve without moving the persistence enum.</summary>
public enum KundaliBirthTimeMode { Exact, Approximate, Unknown }

/// <summary>
/// Calculation conventions for every numeric field in the overview. Held once at the envelope root rather than
/// repeated per value, so a consumer cannot read a number without its conventions being in scope.
/// </summary>
public sealed record KundaliOverviewConventions(
    string EphemerisProvider,
    string EphemerisVersion,
    string Ayanamsa,
    string HouseSystem,
    string NodeType,
    string Zodiac);

/// <summary>
/// The KUN-05 overview slice. Every astrological block is nullable and, when null, its path appears in
/// <see cref="Unsupported"/> with a reason. Null is therefore always explained - never a silent gap.
/// </summary>
public sealed record KundaliOverview(
    Guid ProfileId,
    KundaliBirthDetails BirthDetails,
    KundaliOverviewConventions Conventions,
    KundaliAscendant? Ascendant,
    string? MoonSign,
    string? SunSign,
    KundaliDasha? CurrentDasha,
    IReadOnlyList<KundaliPlacement> KeyPlacements,
    IReadOnlyList<KundaliUnsupportedField> Unsupported);

public sealed record KundaliAscendant(string Sign, decimal DegreeWithinSign, string Nakshatra);

public sealed record KundaliDasha(string Lord, string Level, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>
/// KUN-07 charts section for one divisional chart. The chart style (North/South/East) is deliberately absent: all
/// three styles render the same placements, so style is a presentation concern and never part of the result.
/// </summary>
/// <param name="Divisional">The division actually used, echoed so a caller never infers it from the request.</param>
/// <param name="Precision">Whether the chart's placements are validated; a chart is never published without saying so.</param>
public sealed record KundaliChartsSection(
    int Divisional,
    string Method,
    IReadOnlyList<KundaliChartHouse> Houses,
    KundaliChartPrecision Precision);

/// <summary>One house of the chart: a sign, and the planets placed in it with the exact degree within that sign.</summary>
public sealed record KundaliChartHouse(string Sign, IReadOnlyList<KundaliChartPlacement> Placements);

public sealed record KundaliChartPlacement(string Planet, string Sign, decimal DegreeWithinSign);

/// <summary>
/// Why a chart's placements are or are not trustworthy. <c>Status</c> is <c>validated</c> or <c>unvalidated-reference</c>;
/// the reason is a capability code, never free prose.
/// </summary>
public sealed record KundaliChartPrecision(string Status, string? Reason);



/// <summary>
/// KUN-09 dasha timeline. Every period is an absolute UTC instant pair; <c>DisplayTimezone</c> states the zone a
/// client should render them in, and is carried explicitly rather than inferred at render time.
/// </summary>
/// <remarks>
/// <para><c>MoonLongitude</c> is the sidereal longitude the starting lord was derived from. It is published so a
/// reader can verify the selection instead of trusting it.</para>
/// <para><c>PeriodPath</c> is the mahadasha/antara/pratyantar chain at the query instant, outermost first. It is
/// empty rather than partial when the instant falls outside the generated span.</para>
/// </remarks>
public sealed record KundaliDashaTimeline(
    string System,
    string DisplayTimezone,
    double? MoonLongitude,
    DateTimeOffset? At,
    IReadOnlyList<KundaliDasha> Mahadashas,
    IReadOnlyList<KundaliDasha> PeriodPath,
    IReadOnlyList<KundaliUnsupportedField> Unsupported);



public sealed record KundaliPlacement(string Graha, string Sign, int House, bool Retrograde);

/// <summary>
/// The twelve rashis (sidereal signs) in order. Index 0 is Aries, matching the engine's zero-based output, so a sign
/// index crossing a module boundary is never ambiguous about its origin.
/// </summary>
public static class KundaliSigns
{
    private static readonly string[] Names =
    [
        "Aries", "Taurus", "Gemini", "Cancer", "Leo", "Virgo",
        "Libra", "Scorpio", "Sagittarius", "Capricorn", "Aquarius", "Pisces"
    ];

    /// <summary>All twelve sign names, in zodiacal order. Exposed so a chart can enumerate every house
    /// without duplicating the list.</summary>
    public static IReadOnlyList<string> All { get; } = Names;

    /// <summary>Maps a zero-based sign index to its name. Out-of-range input is a programming error, not data.</summary>
    public static string NameOf(int index) =>
        index is >= 0 and < 12
            ? Names[index]
            : throw new ArgumentOutOfRangeException(nameof(index), index, "A sign index must be 0-11.");
}

