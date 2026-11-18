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

    /// <summary>The profile was edited while this result was being produced, so the result describes inputs that no
    /// longer exist. It is discarded, never shown (KUN-22).</summary>
    public const string SupersededByProfileEdit = "superseded_by_profile_edit";
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
/// <remarks>
/// <para><c>IsStale</c> is computed against the profile's revision as it is right now, not stored: a revision persisted
/// for an older profile revision is stale the moment the profile changes, whether or not a replacement has been
/// generated yet. It is never derived from the cached copy of a result, so a stale cache entry cannot make a stale
/// revision look current.</para>
/// <para><c>SupersededByRevision</c> names the revision that replaced this one when a newer revision has been accepted
/// (KUN-22). A non-null value means the payload must not be presented as current at all.</para>
/// </remarks>
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
    DateTimeOffset CreatedAtUtc,
    long? SupersededByRevision = null);

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
/// <summary>The settings a result was produced under, so a client never has to guess which ayanamsa or
/// chart style applied. <c>Ayanamsa</c> is calculation-affecting; <c>ChartStyle</c> is presentation-only.
/// </summary>
/// <param name="Ayanamsa">Effective ayanamsa from the account Settings. Changing it invalidates results.</param>
/// <param name="ChartStyle">Effective chart style. Presentation only; changing it must never invoke the engine.</param>
public sealed record KundaliEffectiveSettings(string Ayanamsa, string ChartStyle);

/// <summary>What a read returns: the aggregate plus its current revision summary, or a status when no revision
/// has completed yet.</summary>
/// <param name="Aggregate">The aggregate envelope: active revision pointer and optimistic revision.</param>
/// <param name="CurrentRevision">The active revision, or null when none has been accepted yet.</param>
/// <param name="ProfileRevision">The profile input revision the result is judged against (KUN-21/KUN-22).</param>
/// <param name="InputRevision">The profile revision the current result was computed from.</param>
/// <param name="IsCurrent">True only when the current revision succeeded, was computed from the profile present
/// revision, and has not been superseded. Reading this instead of inferring it keeps the staleness rule in one place.</param>
/// <param name="RevisionKey">Opaque, account-scoped cache identity for this exact result: profile, input revision,
/// effective settings and engine/rule versions. Clients echo it back and can key their own query cache on it.</param>
/// <param name="Settings">Effective settings for this read. Nullable with a null default: persistence does not
/// know the account Settings, and inventing a value there would put a plausible-looking ayanamsa on a result it never
/// applied. The facade fills it from the metadata reader on every read, so a null reaching a caller is a bug.</param>
public sealed record KundaliView(
    KundaliAggregate Aggregate,
    KundaliRevisionSummary? CurrentRevision,
    long ProfileRevision = 0,
    long InputRevision = 0,
    bool IsCurrent = false,
    string? RevisionKey = null,
    KundaliEffectiveSettings? Settings = null);

public static class KundaliLimits
{
    public const int ChartStyle = 32;
    /// <summary>Generous bound so a section payload cannot become an unbounded write.</summary>
    public const int SectionPayloadBytes = 1_048_576;
}

/// <summary>
/// What a profile edit changed (KUN-22). The classification decides whether an invariant was broken: a display change
/// (`name`) alters what is shown and nothing else, so it must never cost a calculation. Every other kind can move the
/// birth snapshot or the effective settings a result was computed from.
/// </summary>
public static class KundaliProfileChangeKinds
{
    /// <summary>Profile name only. Display-only: the engine is not called and no revision is created.</summary>
    public const string Display = "display";

    /// <summary>Birth date, birth time, or the birth place/coordinates: the calculation input changed.</summary>
    public const string BirthInput = "birth-input";

    /// <summary>Calculation settings (for example the ayanamsa) changed for the profile.</summary>
    public const string CalculationSettings = "calculation-settings";

    public static readonly IReadOnlyList<string> All = [Display, BirthInput, CalculationSettings];

    /// <summary>Only a display change is exempt from invalidation; anything unrecognised is treated as a real change.</summary>
    public static bool IsDisplayOnly(string changeKind) => string.Equals(changeKind, Display, StringComparison.Ordinal);
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

    /// <summary>KUN-13: the interval exists but has not begun at the reference instant. Not a gap in the calculation -
    /// a fact about the date, told apart from "not known" so a reader is never told the engine failed.</summary>
    public const string NotYetActive = "not-yet-active";

    /// <summary>KUN-13: no transit interval falls in the searched span for this profile's Moon sign.</summary>
    public const string NoIntervalInSpan = "no-interval-in-span";
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
    IReadOnlyList<KundaliUnsupportedField> Unsupported)
{
    /// <summary>The revision freshness envelope (KUN-21). Stamped by the facade, never by the projection that builds
    /// the facts, so a payload can never be published without saying which revision it describes.</summary>
    public KundaliResultFreshness Freshness { get; init; } = KundaliResultFreshness.None;

    /// <summary>The mahadasha chain the current-dasha highlight was selected from, used to derive the expiry boundary
    /// from the same periods. Never persisted and never published: it is cleared before the payload is cached or
    /// returned, so no consumer can read it as a section.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<KundaliDasha>? MahadashaChain { get; init; }

    /// <summary>True when neither the facts nor the profile revision moved between a cached copy and a recomputation,
    /// so the cached copy may be served. Any change means the cached copy is discarded rather than compared field by
    /// field.</summary>
    public bool Matches(KundaliOverview other) =>
        ProfileId == other.ProfileId && Freshness.RevisionKey == other.Freshness.RevisionKey
        && Freshness.CurrentDashaExpiresAtUtc == other.Freshness.CurrentDashaExpiresAtUtc
        && (MahadashaChain ?? []).SequenceEqual(other.MahadashaChain ?? [])
        && Equals(BirthDetails, other.BirthDetails) && Equals(Conventions, other.Conventions)
        && Equals(Ascendant, other.Ascendant) && MoonSign == other.MoonSign && SunSign == other.SunSign
        && Equals(CurrentDasha, other.CurrentDasha)
        && KeyPlacements.SequenceEqual(other.KeyPlacements) && Unsupported.SequenceEqual(other.Unsupported);
}

/// <summary>
/// Revision identity of a result (KUN-21). `RevisionKey` binds the profile, the input revision, the effective
/// calculation settings and every engine/rule version, so an engine upgrade or a profile edit can never be served as a
/// current result. `CurrentDashaExpiresAtUtc` is the exact instant the `currentDasha` value stops describing "now" -
/// the end of the mahadasha containing the response instant - so a cached copy can be revalidated at the boundary
/// instead of after an arbitrary lifetime.
/// </summary>
public sealed record KundaliResultFreshness(
    Guid ProfileId,
    long ProfileRevision,
    string RevisionKey,
    string InputSnapshotHash,
    KundaliProvenance Provenance,
    DateTimeOffset AsOfUtc,
    DateTimeOffset? CurrentDashaExpiresAtUtc)
{
    public static KundaliResultFreshness None { get; } =
        new(Guid.Empty, 0, "", "", new KundaliProvenance("", "", "", "", ""), DateTimeOffset.MinValue, null);

    /// <summary>True when a copy taken at `now` still describes the world: the same revision identity, and the dasha
    /// highlight has not crossed its boundary.</summary>
    public bool IsFreshAt(DateTimeOffset now) =>
        RevisionKey.Length > 0 && (CurrentDashaExpiresAtUtc is not { } boundary || now < boundary);
}

public sealed record KundaliAscendant(string Sign, decimal DegreeWithinSign, string Nakshatra);

public sealed record KundaliDasha(string Lord, string Level, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>
/// KUN-11 ashtakavarga section. <c>RulesVersion</c> identifies the exact contribution tables used, so a later table
/// change is visible rather than silent. <c>Sarva</c> is derived from <c>Bhinna</c>, so the two always reconcile.
/// </summary>
public sealed record KundaliAshtakavargaSection(
    string RulesVersion,
    IReadOnlyDictionary<string, IReadOnlyList<int>> Bhinna,
    IReadOnlyList<int> Sarva,
    KundaliAshtakavargaTotals Totals,
    IReadOnlyList<KundaliUnsupportedField> Unsupported);

/// <summary>Bindu totals: per planet, and the grand total across all seven scored planets.</summary>
public sealed record KundaliAshtakavargaTotals(
    IReadOnlyDictionary<string, int> PerPlanet,
    int GrandTotal);



/// <summary>
/// KUN-08 planets section. Two distinct claims are carried in one payload and must not be conflated:
/// <c>Rules</c> lists the dignity and aspect <b>methods this build implements and tests</b>, while
/// <c>Planets</c> lists <b>actual placements</b>, which are empty when the engine has no position for a graha.
/// </summary>
/// <param name="HouseSystem">The configured house system identifier, echoed rather than assumed.</param>
/// <param name="AspectMethod">The configured aspect method identifier.</param>
/// <param name="Rules">Rule capabilities: implemented and tested, independent of any chart.</param>
/// <param name="Planets">Placements. Empty entries are explained by <paramref name="Unsupported"/>.</param>
public sealed record KundaliPlanetsSection(
    string HouseSystem,
    string AspectMethod,
    KundaliRuleCapabilities Rules,
    IReadOnlyList<KundaliPlanetDetail> Planets,
    IReadOnlyList<KundaliUnsupportedField> Unsupported);

/// <summary>
/// What this build can and cannot answer. Separating "rule implemented" from "placement available" is the whole point:
/// a client must be able to say "we can compute dignity, but not for your chart yet".
/// </summary>
public sealed record KundaliRuleCapabilities(
    IReadOnlyList<string> DignityStates,
    IReadOnlyList<string> AspectMethodIds,
    IReadOnlyList<string> BodiesWithPositions,
    IReadOnlyList<string> BodiesWithoutPositions);

/// <summary>A single placement. Fields that cannot be produced are absent and named in the section's unsupported list.</summary>
public sealed record KundaliPlanetDetail(
    string Planet,
    double Longitude,
    string Sign,
    decimal DegreeInSign,
    string? Nakshatra,
    int? Pada,
    int? House,
    bool? Retrograde,
    bool? Combust,
    string Dignity,
    IReadOnlyList<KundaliPlanetAspect> Aspects);

/// <summary>One whole-sign aspect. There is no orb: whole-sign aspects do not have one.</summary>
public sealed record KundaliPlanetAspect(string To, string Type);



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


/// <summary>
/// KUN-12 Shadbala and Bhava Bala. Two separate top-level objects, never merged into one score: Shadbala measures
/// planetary strength, Bhava Bala measures house strength, and adding them together would be a meaningless number.
/// </summary>
/// <param name="RulesVersion">Identifies the exact component rules and threshold table used.</param>
/// <param name="Unit">The strength unit, echoed so a consumer never assumes.</param>
/// <param name="Complete">False whenever any classical component is missing. A caller must check this before comparing
/// <c>RatioToThreshold</c> to the classical required-rupas table.</param>
/// <param name="Shadbala">Per-planet strength. Empty with an explanation when it cannot be computed.</param>
/// <param name="BhavaBala">Per-house strength. Separate from Shadbala by contract.</param>
/// <param name="Unsupported">Every withheld element and why. Never an unexplained absence.</param>
public sealed record KundaliBalaSection(
    string RulesVersion,
    string Unit,
    bool Complete,
    IReadOnlyList<KundaliShadbalaPlanet> Shadbala,
    IReadOnlyList<KundaliBhavaBalaHouse> BhavaBala,
    IReadOnlyList<KundaliUnsupportedField> Unsupported);

/// <summary>One planet's Shadbala. <paramref name="ComponentsWithheld"/> names the classical components that are
/// missing so the total is never mistaken for a six-fold score.</summary>
/// <param name="ComponentStates">Machine-readable state per component (computed / withheld / placeholder). A client
/// summing <paramref name="Components"/> must consult this rather than treat every number as strength   a placeholder
/// zero and a computed zero are identical numerically.</param>
/// <param name="ComponentsInTotal">Exactly which components the total sums, so the total is never correct only for a
/// reader who also read a prose list.</param>
/// <param name="ComponentsExcludedFromTotal">Components present in the breakdown that do not contribute strength.</param>
public sealed record KundaliShadbalaPlanet(
    string Planet,
    IReadOnlyDictionary<string, double> Components,
    IReadOnlyDictionary<string, string> ComponentStates,
    double Total,
    double? RequiredRupas,
    double? RatioToThreshold,
    IReadOnlyList<string> ComponentsWithheld,
    IReadOnlyList<string> ComponentsInTotal,
    IReadOnlyList<string> ComponentsExcludedFromTotal);

/// <summary>One house's Bhava Bala, with its own component breakdown.</summary>
public sealed record KundaliBhavaBalaHouse(
    int House,
    IReadOnlyDictionary<string, double> Components,
    double Total);

/// <summary>One component's value, its basis, and its machine-readable state, so a number is never bare and a
/// placeholder is never mistaken for a measurement.</summary>
public sealed record KundaliBalaComponent(string Component, double Rupas, string Basis, string State);
/// KUN-13: a reader's opt-in consent for one alert event type on one profile.
/// </summary>
/// <param name="EventType">Event type id from <see cref="KundaliAlertEventTypes"/>.</param>
/// <param name="Channel">Delivery channel. Email only in this phase; the set is closed.</param>
/// <param name="ConsentedAtUtc">When consent for this event type was first granted. Preserved across later updates so
/// the audit trail is not reset by an unrelated change.</param>
public sealed record KundaliAlertPreference(string EventType, string Channel, DateTimeOffset ConsentedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// The alert event catalogue. Published as data so the API, the UI and the delivery path agree on the identifiers, and
/// so a client cannot subscribe to an event this build does not produce.
/// </summary>
public static class KundaliAlertEventTypes
{
    /// <summary>Saturn's entry into the 12th sign from the natal Moon.</summary>
    public const string SadeSatiRising = "sade-sati-rising";

    /// <summary>Saturn's entry into the natal Moon sign.</summary>
    public const string SadeSatiPeak = "sade-sati-peak";

    /// <summary>Saturn's entry into the 2nd sign from the natal Moon.</summary>
    public const string SadeSatiSetting = "sade-sati-setting";

    /// <summary>Saturn's ingress into any sidereal sign.</summary>
    public const string SaturnIngress = "saturn-ingress";

    /// <summary>Jupiter's ingress into any sidereal sign.</summary>
    public const string JupiterIngress = "jupiter-ingress";

    public static readonly IReadOnlyList<string> All =
        [SadeSatiRising, SadeSatiPeak, SadeSatiSetting, SaturnIngress, JupiterIngress];

    /// <summary>Delivery channels approved for this phase. Owner decision #23: email only, no push and no SMS.</summary>
    public static readonly IReadOnlyList<string> Channels = ["email"];
}

/// <summary>
/// KUN-13 transit section for an explicit reference instant.
/// </summary>
/// <remarks>
/// Every value is dated: <c>ReferenceInstant</c> is the instant the reader asked about, <c>ComputedAt</c> when it was
/// computed, and <c>ExpiresAt</c> the point after which the reader should recompute. A transit answer with no reference
/// instant is not an answer, it is a claim about "now" that silently changes.
/// </remarks>
/// <param name="ReferenceInstant">The instant the transit positions describe.</param>
/// <param name="Timezone">The IANA zone the reader asked to see it in, echoed so a display cannot disagree.</param>
/// <param name="Saturn">Saturn's current sign and the ingress windows around the reference instant.</param>
/// <param name="Jupiter">Jupiter's current sign and the ingress windows around the reference instant.</param>
/// <param name="SadeSati">The Sade Sati interval covering or following the reference instant, or null with a reason in
/// <paramref name="Unsupported"/>.</param>
public sealed record KundaliTransitsSection(
    DateTimeOffset ReferenceInstant,
    string Timezone,
    DateTimeOffset ComputedAt,
    DateTimeOffset ExpiresAt,
    KundaliTransitGraha? Saturn,
    KundaliTransitGraha? Jupiter,
    KundaliSadeSati? SadeSati,
    IReadOnlyList<KundaliDatedTransit> DatedTransit,
    IReadOnlyList<KundaliUnsupportedField> Unsupported);

/// <summary>One slow graha's transit: the sign occupied now and the windows either side of the reference instant.</summary>
/// <param name="IsRetrograde">Whether the graha's longitude is decreasing at the reference instant. A retrograde ingress
/// is still an ingress, but a reader planning around it needs to know the graha may re-cross.</param>
public sealed record KundaliTransitGraha(
    string Graha,
    string Sign,
    double? Longitude,
    bool? IsRetrograde,
    IReadOnlyList<KundaliTransitWindow> Windows);

/// <summary>One ingress window, start inclusive and end exclusive so a boundary instant belongs to one window only.</summary>
public sealed record KundaliTransitWindow(string Sign, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>The Sade Sati interval covering the reference instant, with its three phases dated individually.</summary>
/// <param name="IsPartial">True when the interval is clipped by the search span, so its start or end is not a real
/// ingress. Stated rather than hidden: a clipped interval presented as complete would report a phase that has not
/// begun.</param>
public sealed record KundaliSadeSati(
    string Phase,
    bool Active,
    string MoonSign,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsPartial,
    IReadOnlyList<KundaliSadeSatiPhase> Phases);

public sealed record KundaliSadeSatiPhase(string Phase, string Sign, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>A dated transit event in the near future, as the engine computed it. Distinct from an alert: an alert is a
/// subscription, this is an event.</summary>
public sealed record KundaliDatedTransit(string Graha, string EventType, string Sign, DateTimeOffset AtUtc);