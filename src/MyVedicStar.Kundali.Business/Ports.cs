using System.Security.Claims;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>Read-only access to the owned Vedic Profile that supplies birth inputs. Never writes profile data and
/// never exposes a client-supplied owner.</summary>
public interface IKundaliProfileReader
{
    Task<Guid> GetRegisteredAccountIdAsync(ClaimsPrincipal user, CancellationToken token);

    /// <summary>Returns an immutable snapshot for an owned profile, or null when the profile is not owned.
    /// A foreign id and a missing id are indistinguishable here on purpose (the facade maps both to 404).
    /// Reads are performed as the supplied principal so ownership is enforced by the owning module.</summary>
    Task<KundaliInputSnapshot?> GetSnapshotAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);

    /// <summary>Display-only birth details for the overview: the same owned profile, shaped for reading rather than
    /// calculation. Returns null when the profile is not owned, exactly like <see cref="GetSnapshotAsync"/>, so the
    /// two calls can never disagree about ownership.</summary>
    Task<KundaliProfileDisplay?> GetDisplayAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);


    /// <summary>Resolves the effective calculation settings (ayanamsa) from the existing application Settings.
    /// There is no Kundali-specific settings store.</summary>
    Task<string> GetAyanamsaAsync(ClaimsPrincipal user, CancellationToken token);
}

/// <summary>Persistence boundary for the Kundali module. Every keyed operation filters by accountId AND profileId.
/// Implementations must enforce the one-aggregate-per-profile rule atomically and raise
/// <see cref="KundaliConflictException"/> for the loser of a concurrent first generation.</summary>
public interface IKundaliRepository
{
    Task<KundaliView?> GetAsync(Guid accountId, Guid profileId, CancellationToken token);

    /// <summary>Creates the aggregate and its first revision atomically, or returns the existing aggregate when one
    /// already exists for this owner+profile. Idempotent per owner+profile+input hash+config version.</summary>
    Task<KundaliView> EnsureAggregateWithRevisionAsync(Guid accountId, Guid profileId,
        KundaliInputSnapshot snapshot, KundaliProvenance provenance, CancellationToken token);

    /// <summary>Adds a revision to an existing aggregate with an owner-scoped compare-and-swap on the aggregate
    /// revision. A stale revision is a safe conflict, never an overwrite.</summary>
    Task<KundaliView> AddRevisionAsync(Guid accountId, Guid profileId, long aggregateRevision,
        KundaliInputSnapshot snapshot, KundaliProvenance provenance, CancellationToken token);

    /// <summary>Persists one section payload onto a revision and advances the status. A terminal status is never
    /// downgraded by a later intermediate write.</summary>
    Task<KundaliRevisionSummary> SaveSectionAsync(Guid accountId, Guid profileId, Guid revisionId, string section,
        string payloadJson, bool isFinal, string? failureReasonCode, CancellationToken token);

    Task<KundaliPreferences?> GetPreferencesAsync(Guid accountId, Guid profileId, CancellationToken token);
    /// <summary>
    /// KUN-23: deletes every Kundali row owned by this account for this profile - preferences, revisions, snapshots
    /// and the aggregate - in one transaction, and reports how many aggregates were removed.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately a single ordered delete rather than a database cascade. A cascade fires the moment the
    /// profile row disappears, leaving no place to record the intent, no chance to revoke access first, and no
    /// idempotency: a retried request would find nothing to do and could not distinguish that from success.</para>
    /// <para>Returns the count so a caller can tell "there was nothing to delete" from "deletion happened".</para>
    /// </remarks>
    Task<int> DeleteForProfileAsync(Guid accountId, Guid profileId, CancellationToken token);



    /// <summary>Upserts display preferences. Presentation-only: never touches revisions or provenance.</summary>
    Task<KundaliPreferences> SavePreferencesAsync(Guid accountId, Guid profileId, string chartStyle,
        long revision, CancellationToken token);
}

/// <summary>
/// Reads the configured astronomy provider's identity and version metadata. This is a version read, not a
/// calculation: it lets the overview envelope carry real provenance today, so that when the calculation engine
/// (#60) lands the values are already flowing from the same configured provider rather than being backfilled from
/// placeholders. Nothing here computes a position.
/// </summary>
public interface IKundaliAstronomyMetadataReader
{
    Task<KundaliAstronomyMetadata> GetAsync(ClaimsPrincipal user, CancellationToken token);
}

/// <summary>Provider identity and conventions as configured for this account. Values are reported, never inferred.</summary>
public sealed record KundaliAstronomyMetadata(
    string EphemerisProvider,
    string EphemerisVersion,
    string Ayanamsa,
    string HouseSystem,
    string NodeType,
    string Zodiac,
    string AspectMethod = "Parashari-WholeSign");

/// <summary>Owned birth details as shown back to the user. Never used as calculation input; the immutable
/// <see cref="KundaliInputSnapshot"/> is. Time fields are null for the modes that do not carry them, and the mapping
/// to the wire shape omits them rather than substituting a value.</summary>
public sealed record KundaliProfileDisplay(
    string? Name,
    DateOnly BirthDate,
    BirthTimeMode BirthTimeMode,
    TimeOnly? BirthTime,
    TimeOnly? BirthTimeFrom,
    TimeOnly? BirthTimeTo,
    string PlaceName,
    decimal Latitude,
    decimal Longitude,
    string TimezoneId);

/// <summary>
/// The Kundali calculation engine, as seen by the business layer. Declared here rather than referenced directly so
/// Business does not take a dependency on the calculation project - the implementation is supplied by composition,
/// exactly like the profile reader and repository ports.
/// </summary>
public interface IKundaliCalculationEngine
{
    /// <summary>Computes the sign-bearing values for one immutable input. Pure: no clock, no store, no ambient state.</summary>
    KundaliEngineResult Calculate(KundaliEngineInput input);

    /// <summary>Builds the dasha timeline for one input, with the current path at the supplied instant.</summary>
    KundaliEngineDashaTimeline CalculateDashaTimeline(KundaliDashaQuery query);
}

/// <summary>Plain engine input. Built from the owned snapshot; contains no identity and no settings lookups.</summary>
public sealed record KundaliEngineInput(
    DateOnly BirthDate,
    BirthTimeMode BirthTimeMode,
    TimeOnly? BirthTime,
    decimal Latitude,
    decimal Longitude,
    string TimezoneId,
    string Ayanamsa);

/// <summary>
/// Engine output. Nullable values are always explained by a matching <see cref="Gaps"/> entry, so the caller never
/// has to guess whether absence means "cannot compute" or "not built".
/// </summary>
/// <summary>
/// Dasha section input. Carries an explicit query instant when one was supplied, plus the profile timezone so the
/// response can state which zone the display times belong to.
/// </summary>
public sealed record KundaliDashaQuery(KundaliEngineInput Input, DateTimeOffset? At);

/// <summary>
/// Dasha section output: the mahadasha chain, the three-level path at the query instant, and the sidereal moon
/// longitude the starting point was derived from.
/// </summary>
public sealed record KundaliEngineDashaTimeline(
    double? MoonSiderealLongitude,
    IReadOnlyList<KundaliEngineDasha> Mahadashas,
    IReadOnlyList<KundaliEngineDashaLevel> PeriodPath,
    IReadOnlyList<KundaliEngineYoginiPeriod>? YoginiPeriods,
    IReadOnlyList<KundaliEngineCharaPeriod>? CharaSignPeriods,
    IReadOnlyList<KundaliEngineGap> Gaps);

/// <summary>One level of the current period path: Mahadasha, Antara or Pratyantar.</summary>
public sealed record KundaliEngineDashaLevel(string Lord, string Level, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

/// <summary>One Yogini period as the engine produced it.</summary>
public sealed record KundaliEngineYoginiPeriod(string Name, string Ruler, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, bool IsRemainder);

/// <summary>One equal-duration sign period from the Chara progression. Not a validated Chara dasha period.</summary>
public sealed record KundaliEngineCharaPeriod(string Sign, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

public sealed record KundaliEngineResult(
    double? SiderealSunLongitude,
    double? SiderealMoonLongitude,
    int? SunSign,
    int? MoonSign,
    IReadOnlyList<KundaliEngineDasha>? VimshottariMahadashas,
    IReadOnlyList<KundaliEngineYoginiPeriod>? YoginiPeriods,
    IReadOnlyList<KundaliEngineCharaPeriod>? CharaSignPeriods,
    IReadOnlyList<KundaliEngineGap> Gaps);

/// <summary>One Vimshottari mahadasha period, both ends in UTC. Rendering locally belongs to the caller.</summary>
public sealed record KundaliEngineDasha(string Lord, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    bool IsRemainder);

/// <summary>One value the engine withheld, and the capability-specific reason.</summary>
public sealed record KundaliEngineGap(string Path, string Reason);

/// <summary>Reason codes produced by the engine, mirrored in the API's unsupported vocabulary.</summary>
public static class KundaliEngineReasons
{
    /// <summary>The birth time is not exact, so a time-dependent value is genuinely indeterminate.</summary>
    public const string RequiresExactBirthTime = "requires-exact-birth-time";

    /// <summary>Computable, but no independent reference exists to validate it against.</summary>
    public const string UnvalidatedReference = "unvalidated-reference";

    /// <summary>Not implemented yet.</summary>
    public const string NotImplemented = "not-implemented";
}

