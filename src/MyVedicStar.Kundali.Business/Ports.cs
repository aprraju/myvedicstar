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
    /// <summary>Reads the aggregate and its current revision. <paramref name="currentProfileRevision"/> is the profile's
    /// input revision as it is right now; results computed from any other revision are reported as stale rather than
    /// current (KUN-21/KUN-22). Pass null only when the profile revision is genuinely unknown.</summary>
    Task<KundaliView?> GetAsync(Guid accountId, Guid profileId, long? currentProfileRevision,
        CancellationToken token);

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

    /// <summary>Reads one revision's stored section payloads, for the section read paths that serve persisted results
    /// instead of recalculating (KUN-21). Returns null when the revision is not this owner's.</summary>
    Task<KundaliStoredSections?> GetSectionsAsync(Guid accountId, Guid profileId, Guid revisionId,
        CancellationToken token);

    /// <summary>
    /// Applies one profile revision change to this module's state atomically (KUN-22): the aggregate's
    /// <c>ProfileRevision</c> is advanced (compare-and-swap on the previous value), the aggregate revision is bumped and
    /// the in-flight revision, if any, is abandoned with a reason code. <paramref name="cancelledRevisionIds"/> receives
    /// the abandoned revisions so queues can drop their jobs.
    /// </summary>
    /// <remarks>Idempotent on (`profileId`, `newRevision`): a duplicate or out-of-order delivery is a no-op, so a
    /// replayed event can never cancel work that a later revision legitimately started.</remarks>
    Task<KundaliProfileChangeResult> ApplyProfileChangeAsync(Guid accountId, Guid profileId, long oldRevision,
        long newRevision, string changeKind, CancellationToken token);
}

/// <summary>What an applied profile change did, so the caller can report it without re-reading everything.</summary>
/// <param name="Applied">False when the change was already applied (stale or duplicate delivery).</param>
/// <param name="AggregateRevision">The aggregate's revision after the change, for the client's next compare-and-swap.
/// </param>
/// <param name="AbandonedRevisionId">The in-flight revision that was abandoned, if one was running.</param>
public sealed record KundaliProfileChangeResult(
    Guid ProfileId,
    long ProfileRevision,
    bool Applied,
    bool DisplayOnly,
    long AggregateRevision,
    Guid? AbandonedRevisionId);

/// <summary>The stored section payloads of one revision. A null member means that section was never produced.</summary>
public sealed record KundaliStoredSections(
    Guid RevisionId,
    long Revision,
    long ProfileRevision,
    KundaliGenerationStatus Status,
    bool IsPartial,
    string? OverviewJson,
    string? ChartsJson,
    string? DashasJson);

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

    /// <summary>
    /// KUN-13: transit windows and any Sade Sati interval for one input at an explicit reference instant.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Calculate"/> because a transit answer depends on an instant that is not part of the
    /// birth snapshot: folding it into the natal result would make one cached result depend on a moving target.
    /// </remarks>
    KundaliEngineTransits CalculateTransits(KundaliTransitQuery query);
}

/// <summary>
/// Account-scoped cache for Kundali results (KUN-21). An optimisation only: the persisted result is the source of
/// truth, so every failure here degrades to a miss and a recompute, never to an error. Keys are built by
/// <see cref="KundaliCacheKeys"/> and always carry the owner id, so a shared L2 entry is never readable across
/// accounts.
/// </summary>
public interface IKundaliResultCache
{
    /// <summary>Returns the cached value, or null on a miss, an unusable entry, a failure, or when caching is off.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class;

    /// <summary>Stores a value until <paramref name="validUntil"/>. A failure is recorded and swallowed.</summary>
    Task SetAsync<T>(string key, T value, DateTimeOffset validUntil, string policy, CancellationToken token)
        where T : class;

    /// <summary>Removes one entry. Used when a recomputation disagrees with the cached copy.</summary>
    Task RemoveAsync(string key, CancellationToken token);
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
    IReadOnlyList<KundaliEngineGap> Gaps,
    IReadOnlyList<KundaliEngineGrahaPosition>? GrahaPositions = null);

/// <summary>One graha's placement as the engine produced it. <c>Sign</c> is null when no longitude is available.</summary>
public sealed record KundaliEngineGrahaPosition(string Graha, double? SiderealLongitude, string? Sign,
    string? Reason);



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
    IReadOnlyList<KundaliEngineGap> Gaps,
    IReadOnlyList<KundaliEngineGrahaPosition>? GrahaPositions = null);

/// <summary>One Vimshottari mahadasha period, both ends in UTC. Rendering locally belongs to the caller.</summary>
public sealed record KundaliEngineDasha(string Lord, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    bool IsRemainder);

/// <summary>One value the engine withheld, and the capability-specific reason.</summary>
public sealed record KundaliEngineGap(string Path, string Reason);

/// <summary>
/// KUN-13 transit request: the birth inputs plus the reference instant the transit positions describe.
/// </summary>
/// <remarks>
/// The reference instant is supplied by the caller and never read from a clock here, so a transit answer is
/// reproducible and a test asserting an interval is asserting the engine, not the wall clock.
/// </remarks>
public sealed record KundaliTransitQuery(KundaliEngineInput Input, DateTimeOffset At, string Timezone);

/// <summary>
/// KUN-13 transit output: both slow grahas' current sign and ingress windows, and the Sade Sati interval covering or
/// following the reference instant.
/// </summary>
/// <param name="SadeSatiMoonSign">The natal Moon sign the phases were measured from, echoed so the interval is checkable.</param>
public sealed record KundaliEngineTransits(
    string SaturnSign,
    IReadOnlyList<KundaliSignWindow> SaturnWindows,
    string JupiterSign,
    IReadOnlyList<KundaliSignWindow> JupiterWindows,
    KundaliEngineSadeSati? SadeSati,
    IReadOnlyList<KundaliEngineGap> Gaps);

/// <summary>One ingress window as the engine produced it.</summary>
public sealed record KundaliSignWindow(string Sign, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>The Sade Sati interval and its named phases.</summary>
public sealed record KundaliEngineSadeSati(string MoonSign, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    bool IsPartial, IReadOnlyList<KundaliEngineSadeSatiPhase> Phases);

public sealed record KundaliEngineSadeSatiPhase(string Phase, string Sign, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

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


/// <summary>
/// KUN-13: opt-in alert consent, stored per owner, profile and event type.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="IKundaliRepository"/> because consent has a different lifecycle from calculation
/// results: it survives revision churn, it is deleted with the profile, and it is read at delivery time by a path that
/// must never touch result rows.
/// </remarks>
public interface IKundaliAlertRepository
{
    /// <summary>Every consent row for one owned profile. Empty when the reader has opted out of everything.</summary>
    Task<IReadOnlyList<KundaliAlertPreference>> GetAsync(Guid accountId, Guid profileId, CancellationToken token);

    /// <summary>
    /// Grants consent for the supplied event types on the supplied channel, idempotently.
    /// </summary>
    /// <returns>The resulting full set of consents for the profile, so a caller never has to re-read to render state.</returns>
    /// <remarks>
    /// Idempotent by unique index rather than by read-then-write: two concurrent identical PUTs both succeed and leave
    /// one row, which is the behaviour the architect's "duplicate alert prevention on repeated PUT" test asks for.
    /// An already-consented event keeps its original <c>ConsentedAtUtc</c>; only new grants set it.
    /// </remarks>
    Task<IReadOnlyList<KundaliAlertPreference>> GrantAsync(Guid accountId, Guid profileId,
        IReadOnlyCollection<string> eventTypes, string channel, CancellationToken token);

    /// <summary>Revokes every consent for the profile. Returns the number removed.</summary>
    Task<int> RevokeAllAsync(Guid accountId, Guid profileId, CancellationToken token);

    /// <summary>
    /// Every consent row for one profile, read at delivery time so consent is re-checked rather than remembered.
    /// </summary>
    Task<IReadOnlyList<KundaliAlertPreference>> GetForDeliveryAsync(Guid accountId, Guid profileId,
        CancellationToken token);
}
