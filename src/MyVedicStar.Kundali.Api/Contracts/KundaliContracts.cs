namespace MyVedicStar.API.Contracts;

/// <summary>Revision summary. Failure is reported as a stable reason code, never an engine message, so no internal
/// detail or personal data can appear in an error body.</summary>
public sealed record KundaliRevisionResponse(
    Guid RevisionId,
    long Revision,
    string Status,
    string? FailureReasonCode,
    string InputSnapshotHash,
    bool IsStale,
    bool IsPartial,
    string[] CompletedSections,
    KundaliProvenanceResponse Provenance,
    DateTimeOffset CreatedAtUtc,
    long? SupersededByRevision)
{
    public static KundaliRevisionResponse From(MyVedicStar.Kundali.Shared.KundaliRevisionSummary value) => new(
        value.RevisionId, value.Revision, value.Status.ToString(), value.FailureReasonCode, value.InputSnapshotHash,
        value.IsStale, value.IsPartial, value.CompletedSections.ToArray(), KundaliProvenanceResponse.From(value.Provenance),
        value.CreatedAtUtc, value.SupersededByRevision);
}

public sealed record KundaliProvenanceResponse(
    string EngineVersion, string EphemerisVersion, string RuleSetVersion, string TimezoneDataVersion, string Ayanamsa)
{
    public static KundaliProvenanceResponse From(MyVedicStar.Kundali.Shared.KundaliProvenance value) => new(
        value.EngineVersion, value.EphemerisVersion, value.RuleSetVersion, value.TimezoneDataVersion, value.Ayanamsa);
}

/// <summary>Module shell payload. <c>HasResult</c> is false when the profile has no Kundali yet, which is a valid
/// empty state rather than an error (KUN-01/KUN-02 shell contract).</summary>
/// <summary>
/// Module shell payload. `HasResult` is false when the profile has no Kundali yet, which is a valid empty state rather
/// than an error (KUN-01/KUN-02 shell contract). `IsCurrent`, `ProfileRevision`, `InputRevision` and `RevisionKey`
/// are the freshness envelope (KUN-21): a client renders a result only when `IsCurrent` is true, and keys its own cache
/// on `RevisionKey` - never on the aggregate id, which survives edits.
/// </summary>
public sealed record KundaliViewResponse(
    Guid ProfileId,
    Guid AggregateId,
    long AggregateRevision,
    bool HasResult,
    KundaliRevisionResponse? CurrentRevision,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long ProfileRevision,
    long InputRevision,
    bool IsCurrent,
    string? RevisionKey,
    KundaliSettingsResponse? Settings)
{
    public static KundaliViewResponse From(MyVedicStar.Kundali.Shared.KundaliView value) => new(
        value.Aggregate.ProfileId, value.Aggregate.Id, value.Aggregate.Revision, value.CurrentRevision is not null,
        value.CurrentRevision is null ? null : KundaliRevisionResponse.From(value.CurrentRevision),
        value.Aggregate.CreatedAtUtc == DateTimeOffset.MinValue ? null : value.Aggregate.CreatedAtUtc,
        value.Aggregate.UpdatedAtUtc == DateTimeOffset.MinValue ? null : value.Aggregate.UpdatedAtUtc,
        value.ProfileRevision, value.InputRevision, value.IsCurrent, value.RevisionKey,
        value.Settings is null ? null : KundaliSettingsResponse.From(value.Settings));
}

/// <summary>Effective settings under which a read should be interpreted. <c>Ayanamsa</c> affects calculation;
/// <c>ChartStyle</c> is presentation-only.</summary>
public sealed record KundaliSettingsResponse(string Ayanamsa, string ChartStyle)
{
    public static KundaliSettingsResponse From(MyVedicStar.Kundali.Shared.KundaliEffectiveSettings value) =>
        new(value.Ayanamsa, value.ChartStyle);
}

/// <summary>Freshness envelope of a section payload (KUN-21), so a cached copy always says which revision it describes.</summary>
public sealed record KundaliFreshnessResponse(
    Guid ProfileId,
    long ProfileRevision,
    string RevisionKey,
    string InputSnapshotHash,
    KundaliProvenanceResponse Provenance,
    DateTimeOffset AsOfUtc,
    DateTimeOffset? CurrentDashaExpiresAtUtc)
{
    public static KundaliFreshnessResponse? From(MyVedicStar.Kundali.Shared.KundaliResultFreshness value) =>
        value.RevisionKey.Length == 0
            ? null
            : new KundaliFreshnessResponse(value.ProfileId, value.ProfileRevision, value.RevisionKey,
                value.InputSnapshotHash, KundaliProvenanceResponse.From(value.Provenance), value.AsOfUtc,
                value.CurrentDashaExpiresAtUtc);
}

public sealed record KundaliPreferencesResponse(string ChartStyle, long Revision, DateTimeOffset? UpdatedAtUtc)
{
    public static KundaliPreferencesResponse From(MyVedicStar.Kundali.Shared.KundaliPreferences value) => new(
        value.ChartStyle, value.Revision, value.UpdatedAtUtc == DateTimeOffset.MinValue ? null : value.UpdatedAtUtc);
}

/// <summary>Replace payload carries the revision required for optimistic concurrency.</summary>
public sealed record SaveKundaliPreferencesRequest(string ChartStyle, long Revision);

/// <summary>KUN-14 interpretations response (api#54). Mirrors the shared section one field per field so the wire shape
/// cannot drift from the business contract.</summary>
public sealed record KundaliInterpretationsResponse(
    long SourceRevision,
    string PromptVersion,
    string ModelProvenance,
    string Locale,
    string InterpretationLabel,
    IReadOnlyList<KundaliInterpretationResponse> Sections,
    IReadOnlyList<KundaliUnsupportedResponse> Unsupported)
{
    public static KundaliInterpretationsResponse From(MyVedicStar.Kundali.Shared.KundaliInterpretationsSection value) =>
        new(value.SourceRevision, value.PromptVersion, value.ModelProvenance, value.Locale, value.InterpretationLabel,
            value.Sections.Select(KundaliInterpretationResponse.From).ToArray(),
            value.Unsupported.Select(KundaliUnsupportedResponse.From).ToArray());
}

/// <summary>One domain: its status, its reason, and the factors the text rests on. <c>text</c> is null unless
/// <c>status</c> is <c>ready</c>.</summary>
public sealed record KundaliInterpretationResponse(
    string Domain,
    string Status,
    string? Text,
    IReadOnlyList<KundaliInterpretationFactorResponse> Factors,
    string? Reason,
    IReadOnlyList<string> UnknownFactorReferences)
{
    public static KundaliInterpretationResponse From(MyVedicStar.Kundali.Shared.KundaliInterpretationEntry value) =>
        new(value.Domain, value.Status, value.Text,
            value.Factors.Select(KundaliInterpretationFactorResponse.From).ToArray(),
            value.Reason,
            value.UnknownFactorReferences.ToArray());
}

/// <summary>One citable fact, by stable id.</summary>
public sealed record KundaliInterpretationFactorResponse(string Id, string Description)
{
    public static KundaliInterpretationFactorResponse From(MyVedicStar.Kundali.Shared.KundaliInterpretationFactor value) =>
        new(value.Id, value.Description);
}

/// <summary>KUN-14 varshaphal: the only shape the engine supports, which is not-available.</summary>
public sealed record KundaliVarshaphalResponse(bool Available, string Reason)
{
    public static KundaliVarshaphalResponse From(MyVedicStar.Kundali.Shared.KundaliVarshaphalSection value) =>
        new(value.Available, value.Reason);
}

/// <summary>The published capability boundary (ADR-0016 section 3), returned as data.</summary>
public sealed record KundaliCapabilitiesResponse(
    int[] SupportedDivisionalCharts,
    int[] ProvisionalDivisionalCharts,
    string[] SupportedShadbalaComponents,
    string[] UnsupportedShadbalaComponents,
    string LunarNodeConvention,
    string AscendantConvention,
    DateOnly MinimumSupportedBirthDate,
    DateOnly MaximumSupportedBirthDate,
    string[] UnsupportedCapabilities,
    /// <summary>Closed set of section ids a report may request. Published so the client binds to these ids rather
    /// than asserting the server's structure.</summary>
    string[] ReportSections)
{
    public static KundaliCapabilitiesResponse From(MyVedicStar.Kundali.Shared.KundaliCapabilityReport value) => new(
        value.SupportedDivisionalCharts.ToArray(), value.ProvisionalDivisionalCharts.ToArray(),
        value.SupportedShadbalaComponents.ToArray(), value.UnsupportedShadbalaComponents.ToArray(),
        value.LunarNodeConvention, value.AscendantConvention, value.MinimumSupportedBirthDate,
        value.MaximumSupportedBirthDate, value.UnsupportedCapabilities.ToArray(),
        value.ReportSections.ToArray());
}

/// <summary>KUN-05 overview response. Every astrological block is nullable and, when null, its path appears in
/// <c>Unsupported</c> with a reason code - so a gap is always explained rather than silently absent.</summary>
public sealed record KundaliOverviewResponse(
    Guid ProfileId,
    KundaliBirthDetailsResponse BirthDetails,
    KundaliConventionsResponse Conventions,
    KundaliAscendantResponse? Ascendant,
    string? MoonSign,
    string? SunSign,
    KundaliDashaResponse? CurrentDasha,
    IReadOnlyList<KundaliPlacementResponse> KeyPlacements,
    IReadOnlyList<KundaliUnsupportedResponse> Unsupported,
    KundaliFreshnessResponse? Freshness)
{
    public static KundaliOverviewResponse From(MyVedicStar.Kundali.Shared.KundaliOverview value) => new(
        value.ProfileId,
        KundaliBirthDetailsResponse.From(value.BirthDetails),
        KundaliConventionsResponse.From(value.Conventions),
        value.Ascendant is null ? null : KundaliAscendantResponse.From(value.Ascendant),
        value.MoonSign,
        value.SunSign,
        value.CurrentDasha is null ? null : KundaliDashaResponse.From(value.CurrentDasha),
        value.KeyPlacements.Select(KundaliPlacementResponse.From).ToArray(),
        value.Unsupported.Select(KundaliUnsupportedResponse.From).ToArray(),
        KundaliFreshnessResponse.From(value.Freshness));
}


/// <summary>KUN-09 dashas. Period bounds are absolute UTC instants; <c>DisplayTimezone</c> names the zone to render
/// them in, so the client never guesses. <c>MoonLongitude</c> is published so the starting lord can be checked.</summary>
public sealed record KundaliDashaTimelineResponse(
    string System,
    string DisplayTimezone,
    double? MoonLongitude,
    DateTimeOffset? At,
    IReadOnlyList<KundaliDashaResponse> Mahadashas,
    IReadOnlyList<KundaliDashaResponse> PeriodPath,
    IReadOnlyList<KundaliUnsupportedResponse> Unsupported)
{
    public static KundaliDashaTimelineResponse From(MyVedicStar.Kundali.Shared.KundaliDashaTimeline value) => new(
        value.System,
        value.DisplayTimezone,
        value.MoonLongitude,
        value.At,
        value.Mahadashas.Select(KundaliDashaResponse.From).ToArray(),
        value.PeriodPath.Select(KundaliDashaResponse.From).ToArray(),
        value.Unsupported.Select(KundaliUnsupportedResponse.From).ToArray());
}



/// <summary>KUN-07 charts response. Style is absent by design: North/South/East render the same placements.</summary>
public sealed record KundaliChartsSectionResponse(
    int Divisional,
    string Method,
    IReadOnlyList<KundaliChartHouseResponse> Houses,
    KundaliChartPrecisionResponse Precision)
{
    public static KundaliChartsSectionResponse From(MyVedicStar.Kundali.Shared.KundaliChartsSection value) => new(
        value.Divisional,
        value.Method,
        value.Houses.Select(KundaliChartHouseResponse.From).ToArray(),
        KundaliChartPrecisionResponse.From(value.Precision));
}

public sealed record KundaliChartHouseResponse(string Sign, IReadOnlyList<KundaliChartPlacementResponse> Placements)
{
    public static KundaliChartHouseResponse From(MyVedicStar.Kundali.Shared.KundaliChartHouse value) => new(
        value.Sign, value.Placements.Select(KundaliChartPlacementResponse.From).ToArray());
}

public sealed record KundaliChartPlacementResponse(string Planet, string Sign, decimal DegreeWithinSign)
{
    public static KundaliChartPlacementResponse From(MyVedicStar.Kundali.Shared.KundaliChartPlacement value) =>
        new(value.Planet, value.Sign, value.DegreeWithinSign);
}

public sealed record KundaliChartPrecisionResponse(string Status, string? Reason)
{
    public static KundaliChartPrecisionResponse From(MyVedicStar.Kundali.Shared.KundaliChartPrecision value) =>
        new(value.Status, value.Reason);
}



/// <summary>KUN-08 planets response. <c>Rules</c> and <c>Planets</c> make different claims and must be read
/// separately: rules are implemented and tested, placements are what the engine could actually produce.</summary>
public sealed record KundaliPlanetsSectionResponse(
    string HouseSystem,
    string AspectMethod,
    KundaliRuleCapabilitiesResponse Rules,
    IReadOnlyList<KundaliPlanetDetailResponse> Planets,
    IReadOnlyList<KundaliUnsupportedResponse> Unsupported)
{
    public static KundaliPlanetsSectionResponse From(MyVedicStar.Kundali.Shared.KundaliPlanetsSection value) => new(
        value.HouseSystem,
        value.AspectMethod,
        KundaliRuleCapabilitiesResponse.From(value.Rules),
        value.Planets.Select(KundaliPlanetDetailResponse.From).ToArray(),
        value.Unsupported.Select(KundaliUnsupportedResponse.From).ToArray());
}

public sealed record KundaliRuleCapabilitiesResponse(
    IReadOnlyList<string> DignityStates,
    IReadOnlyList<string> AspectMethodIds,
    IReadOnlyList<string> BodiesWithPositions,
    IReadOnlyList<string> BodiesWithoutPositions)
{
    public static KundaliRuleCapabilitiesResponse From(MyVedicStar.Kundali.Shared.KundaliRuleCapabilities value) =>
        new(value.DignityStates.ToArray(), value.AspectMethodIds.ToArray(),
            value.BodiesWithPositions.ToArray(), value.BodiesWithoutPositions.ToArray());
}

public sealed record KundaliPlanetDetailResponse(
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
    IReadOnlyList<KundaliPlanetAspectResponse> Aspects)
{
    public static KundaliPlanetDetailResponse From(MyVedicStar.Kundali.Shared.KundaliPlanetDetail value) => new(
        value.Planet, value.Longitude, value.Sign, value.DegreeInSign, value.Nakshatra, value.Pada, value.House,
        value.Retrograde, value.Combust, value.Dignity,
        value.Aspects.Select(KundaliPlanetAspectResponse.From).ToArray());
}

public sealed record KundaliPlanetAspectResponse(string To, string Type)
{
    public static KundaliPlanetAspectResponse From(MyVedicStar.Kundali.Shared.KundaliPlanetAspect value) =>
        new(value.To, value.Type);
}



/// <summary>KUN-11 ashtakavarga response. <c>RulesVersion</c> identifies the tables used; <c>Sarva</c> is derived from
/// <c>Bhinna</c>, so the two always reconcile.</summary>
public sealed record KundaliAshtakavargaResponse(
    string RulesVersion,
    IReadOnlyDictionary<string, IReadOnlyList<int>> Bhinna,
    IReadOnlyList<int> Sarva,
    KundaliAshtakavargaTotalsResponse Totals,
    IReadOnlyList<KundaliUnsupportedResponse> Unsupported)
{
    public static KundaliAshtakavargaResponse From(MyVedicStar.Kundali.Shared.KundaliAshtakavargaSection value) => new(
        value.RulesVersion,
        value.Bhinna.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int>)pair.Value.ToArray(),
            StringComparer.Ordinal),
        value.Sarva.ToArray(),
        KundaliAshtakavargaTotalsResponse.From(value.Totals),
        value.Unsupported.Select(KundaliUnsupportedResponse.From).ToArray());
}

/// <summary>
/// KUN-15 comparison response. Mirrors the shared contract with no reshaping, so the UI receives exactly what the
/// business layer decided - in particular the scored/indeterminate discriminator, which a DTO rewrite could silently
/// flatten into a nullable number.
/// </summary>
/// <remarks>
/// There is no threshold, verdict or "match" field. That is a contract decision, not an omission: the owner has left
/// the minimum acceptable-match score open, so the API reports a score and declines to judge it.
/// </remarks>
public sealed record KundaliAshtakootaComparisonResponse(
    string PairKey,
    KundaliComparisonSideResponse ProfileA,
    KundaliComparisonSideResponse ProfileB,
    IReadOnlyList<KundaliAshtakootaCategoryResponse> Categories,
    int Total,
    int ScoredMaximum,
    int Maximum,
    string Certainty,
    KundaliManglikComparisonResponse Manglik,
    string RulesVersion)
{
    public static KundaliAshtakootaComparisonResponse From(
        MyVedicStar.Kundali.Shared.KundaliAshtakootaComparison value) => new(
        value.PairKey,
        KundaliComparisonSideResponse.From(value.ProfileA),
        KundaliComparisonSideResponse.From(value.ProfileB),
        value.Categories.Select(KundaliAshtakootaCategoryResponse.From).ToArray(),
        value.Total,
        value.ScoredMaximum,
        value.Maximum,
        value.Certainty,
        KundaliManglikComparisonResponse.From(value.Manglik),
        value.RulesVersion);
}

public sealed record KundaliComparisonSideResponse(Guid ProfileId, long Revision)
{
    public static KundaliComparisonSideResponse From(MyVedicStar.Kundali.Shared.KundaliComparisonSide value) =>
        new(value.ProfileId, value.Revision);
}

/// <summary>One koota. <c>Score</c> null means indeterminate and <c>Reason</c> then says why; both are never set.</summary>
public sealed record KundaliAshtakootaCategoryResponse(
    string Code,
    string Name,
    int Maximum,
    int? Score,
    string? Reason,
    string? Side,
    string Note)
{
    public static KundaliAshtakootaCategoryResponse From(
        MyVedicStar.Kundali.Shared.KundaliAshtakootaCategory value) =>
        new(value.Code, value.Name, value.Maximum, value.Score, value.Reason, value.Side, value.Note);
}

public sealed record KundaliManglikComparisonResponse(string StatusA, string StatusB, string Note)
{
    public static KundaliManglikComparisonResponse From(
        MyVedicStar.Kundali.Shared.KundaliManglikComparison value) =>
        new(value.StatusA, value.StatusB, value.Note);
}

public sealed record KundaliAshtakavargaTotalsResponse(
    IReadOnlyDictionary<string, int> PerPlanet,
    int GrandTotal)
{
    public static KundaliAshtakavargaTotalsResponse From(MyVedicStar.Kundali.Shared.KundaliAshtakavargaTotals value) =>
        new(value.PerPlanet.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), value.GrandTotal);
}


/// <summary>Time fields are omitted when the mode does not carry them, so no consumer can read a placeholder as a
/// real birth time.</summary>
public sealed record KundaliBirthDetailsResponse(
    string? Name,
    DateOnly BirthDate,
    string BirthTimeMode,
    TimeOnly? BirthTime,
    TimeOnly? WindowStart,
    TimeOnly? WindowEnd,
    string PlaceName,
    decimal Latitude,
    decimal Longitude,
    string TimeZoneId)
{
    public static KundaliBirthDetailsResponse From(MyVedicStar.Kundali.Shared.KundaliBirthDetails value) => new(
        value.Name, value.BirthDate, value.BirthTimeMode.ToString().ToLowerInvariant(), value.BirthTime,
        value.BirthTimeFrom, value.BirthTimeTo, value.PlaceName, value.Latitude, value.Longitude, value.TimezoneId);
}

public sealed record KundaliConventionsResponse(
    string EphemerisProvider, string EphemerisVersion, string Ayanamsa, string HouseSystem, string NodeType,
    string Zodiac)
{
    public static KundaliConventionsResponse From(MyVedicStar.Kundali.Shared.KundaliOverviewConventions value) => new(
        value.EphemerisProvider, value.EphemerisVersion, value.Ayanamsa, value.HouseSystem, value.NodeType,
        value.Zodiac);
}

public sealed record KundaliAscendantResponse(string Sign, decimal DegreeWithinSign, string Nakshatra)
{
    public static KundaliAscendantResponse From(MyVedicStar.Kundali.Shared.KundaliAscendant value) =>
        new(value.Sign, value.DegreeWithinSign, value.Nakshatra);
}

public sealed record KundaliDashaResponse(string Lord, string Level, DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public static KundaliDashaResponse From(MyVedicStar.Kundali.Shared.KundaliDasha value) =>
        new(value.Lord, value.Level, value.StartUtc, value.EndUtc);
}

public sealed record KundaliPlacementResponse(string Graha, string Sign, int House, bool Retrograde)
{
    public static KundaliPlacementResponse From(MyVedicStar.Kundali.Shared.KundaliPlacement value) =>
        new(value.Graha, value.Sign, value.House, value.Retrograde);
}

/// <summary>A field that could not be produced, with a reason code the UI can distinguish from birth-time
/// uncertainty.</summary>
public sealed record KundaliUnsupportedResponse(string Path, string Reason)
{
    public static KundaliUnsupportedResponse From(MyVedicStar.Kundali.Shared.KundaliUnsupportedField value) =>
        new(value.Path, value.Reason);
}


/// <summary>KUN-12 Shadbala and Bhava Bala. Two separate top-level objects by contract: planetary strength and house
/// strength are different quantities and merging them into one score would produce a meaningless number.</summary>
/// <param name="Complete">False while any classical component is missing. A consumer must check this before comparing
/// <c>ratioToThreshold</c> to the classical required-rupas table.</param>
public sealed record KundaliBalaResponse(
    string RulesVersion,
    string Unit,
    bool Complete,
    IReadOnlyList<KundaliShadbalaPlanetResponse> Shadbala,
    IReadOnlyList<KundaliBhavaBalaHouseResponse> BhavaBala,
    IReadOnlyList<KundaliUnsupportedResponse> Unsupported)
{
    public static KundaliBalaResponse From(MyVedicStar.Kundali.Shared.KundaliBalaSection value) => new(
        value.RulesVersion,
        value.Unit,
        value.Complete,
        value.Shadbala.Select(KundaliShadbalaPlanetResponse.From).ToArray(),
        value.BhavaBala.Select(KundaliBhavaBalaHouseResponse.From).ToArray(),
        value.Unsupported.Select(KundaliUnsupportedResponse.From).ToArray());
}

/// <summary>One planet's strength. <c>componentsWithheld</c> names the classical components that are missing, so a
/// partial total is never mistaken for a six-fold score.</summary>
/// <remarks><b><c>components</c> cannot be summed, or ranked, as strength; <c>total</c> is the strength.</b> The
/// dictionary discloses the parts of <c>total</c> so a client can audit it, not a set of numbers to add up:
/// <c>total</c> sums exactly the members of <c>componentsInTotal</c> and excludes <c>componentsExcludedFromTotal</c>.
/// <para>A client summing <c>components</c> is wrong twice over, and silently. A withheld component (see
/// <c>componentsWithheld</c>) is a key that is simply absent, so the sum drops an unknown quantity without changing
/// shape. A placeholder component is present with a real-looking number (<c>dig</c> is <c>0.0</c>), so the sum adds a
/// zero that was never strength and is numerically identical to a measured zero.</para>
/// <para><c>componentStates</c> is total over <c>components</c> - one state per key - and is the only discriminator
/// between a measured value and a placeholder. Read <c>total</c> for strength, and gate any comparison of
/// <c>ratioToThreshold</c> on the section's <c>complete</c> flag.</para></remarks>
public sealed record KundaliShadbalaPlanetResponse(
    string Planet,
    IReadOnlyDictionary<string, double> Components,
    IReadOnlyDictionary<string, string> ComponentStates,
    double Total,
    double? RequiredRupas,
    double? RatioToThreshold,
    IReadOnlyList<string> ComponentsWithheld,
    IReadOnlyList<string> ComponentsInTotal,
    IReadOnlyList<string> ComponentsExcludedFromTotal)
{
    public static KundaliShadbalaPlanetResponse From(
        MyVedicStar.Kundali.Shared.KundaliShadbalaPlanet value) => new(
        value.Planet,
        value.Components,
        value.ComponentStates,
        value.Total,
        value.RequiredRupas,
        value.RatioToThreshold,
        value.ComponentsWithheld,
        value.ComponentsInTotal,
        value.ComponentsExcludedFromTotal);
}

/// <summary>One house's strength, with its own component breakdown.</summary>
public sealed record KundaliBhavaBalaHouseResponse(
    int House,
    IReadOnlyDictionary<string, double> Components,
    double Total)
{
    public static KundaliBhavaBalaHouseResponse From(
        MyVedicStar.Kundali.Shared.KundaliBhavaBalaHouse value) => new(
        value.House,
        value.Components,
        value.Total);
}

/// <summary>
/// KUN-13 transit response. Every number is accompanied by the instant it describes, the instant it was computed and
/// the instant it expires, so a client never has to guess freshness.
/// </summary>
public sealed record KundaliTransitsSectionResponse(
    DateTimeOffset ReferenceInstant,
    string Timezone,
    DateTimeOffset ComputedAt,
    DateTimeOffset ExpiresAt,
    KundaliTransitGrahaResponse? Saturn,
    KundaliTransitGrahaResponse? Jupiter,
    KundaliSadeSatiResponse? SadeSati,
    IReadOnlyList<KundaliDatedTransitResponse> DatedTransits,
    IReadOnlyList<KundaliUnsupportedResponse> Unsupported)
{
    public static KundaliTransitsSectionResponse From(MyVedicStar.Kundali.Shared.KundaliTransitsSection value) => new(
        value.ReferenceInstant,
        value.Timezone,
        value.ComputedAt,
        value.ExpiresAt,
        value.Saturn is null ? null : KundaliTransitGrahaResponse.From(value.Saturn),
        value.Jupiter is null ? null : KundaliTransitGrahaResponse.From(value.Jupiter),
        value.SadeSati is null ? null : KundaliSadeSatiResponse.From(value.SadeSati),
        value.DatedTransit.Select(KundaliDatedTransitResponse.From).ToArray(),
        value.Unsupported.Select(KundaliUnsupportedResponse.From).ToArray());
}

public sealed record KundaliTransitGrahaResponse(
    string Graha,
    string Sign,
    double? Longitude,
    bool? IsRetrograde,
    IReadOnlyList<KundaliTransitWindowResponse> Windows)
{
    public static KundaliTransitGrahaResponse From(MyVedicStar.Kundali.Shared.KundaliTransitGraha value) => new(
        value.Graha, value.Sign, value.Longitude, value.IsRetrograde,
        value.Windows.Select(KundaliTransitWindowResponse.From).ToArray());
}

/// <summary>Start inclusive, end exclusive: a boundary instant belongs to exactly one window.</summary>
public sealed record KundaliTransitWindowResponse(string Sign, DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public static KundaliTransitWindowResponse From(MyVedicStar.Kundali.Shared.KundaliTransitWindow value) =>
        new(value.Sign, value.StartUtc, value.EndUtc);
}

public sealed record KundaliSadeSatiResponse(
    string Phase,
    bool Active,
    string MoonSign,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsPartial,
    IReadOnlyList<KundaliSadeSatiPhaseResponse> Phases)
{
    public static KundaliSadeSatiResponse From(MyVedicStar.Kundali.Shared.KundaliSadeSati value) => new(
        value.Phase, value.Active, value.MoonSign, value.StartUtc, value.EndUtc, value.IsPartial,
        value.Phases.Select(KundaliSadeSatiPhaseResponse.From).ToArray());
}

public sealed record KundaliSadeSatiPhaseResponse(string Phase, string Sign, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc)
{
    public static KundaliSadeSatiPhaseResponse From(MyVedicStar.Kundali.Shared.KundaliSadeSatiPhase value) =>
        new(value.Phase, value.Sign, value.StartUtc, value.EndUtc);
}

public sealed record KundaliDatedTransitResponse(string Graha, string EventType, string Sign, DateTimeOffset AtUtc)
{
    public static KundaliDatedTransitResponse From(MyVedicStar.Kundali.Shared.KundaliDatedTransit value) =>
        new(value.Graha, value.EventType, value.Sign, value.AtUtc);
}

/// <summary>
/// One alert consent as returned. <c>Channel</c> is present because a client must be able to show which channel it
/// consented to, and the server validates it rather than trusting the request.
/// </summary>
public sealed record KundaliAlertPreferenceResponse(
    string EventType,
    string Channel,
    DateTimeOffset ConsentedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static KundaliAlertPreferenceResponse From(MyVedicStar.Kundali.Shared.KundaliAlertPreference value) =>
        new(value.EventType, value.Channel, value.ConsentedAtUtc, value.UpdatedAtUtc);
}

/// <summary>KUN-13 alert subscription request. <c>EventTypes</c> is required; an empty list is a 400, not a no-op.</summary>
public sealed record KundaliAlertSubscriptionRequest(IReadOnlyList<string> EventTypes, string Channel);

/// <summary>The full consent state for a profile, so the client never has to merge a response into cached state.</summary>
public sealed record KundaliAlertsResponse(IReadOnlyList<KundaliAlertPreferenceResponse> Subscriptions,
    IReadOnlyList<string> SupportedEventTypes, IReadOnlyList<string> SupportedChannels)
{
    public static KundaliAlertsResponse From(IReadOnlyList<MyVedicStar.Kundali.Shared.KundaliAlertPreference> values) =>
        new(values.Select(KundaliAlertPreferenceResponse.From).ToArray(),
            MyVedicStar.Kundali.Shared.KundaliAlertEventTypes.All.ToArray(),
            MyVedicStar.Kundali.Shared.KundaliAlertEventTypes.Channels.ToArray());
}

/// <summary>KUN-10 yoga and dosha section. <c>evaluated</c> carries finished claims; <c>notEvaluated</c> carries
/// combinations that were never tested, enumerated by name. Both in one payload, each with its own completeness -
/// an empty evaluated list is never a clean bill of health.</summary>
public sealed record KundaliYogasResponse(
    string RulesVersion,
    IReadOnlyList<KundaliYogaAssessmentResponse> Evaluated,
    IReadOnlyList<KundaliYogaAssessmentResponse> NotEvaluated,
    IReadOnlyList<KundaliUnsupportedResponse> Unsupported)
{
    public static KundaliYogasResponse From(MyVedicStar.Kundali.Shared.KundaliYogasSection value) => new(
        value.RulesVersion,
        value.Evaluated.Select(KundaliYogaAssessmentResponse.From).ToArray(),
        value.NotEvaluated.Select(KundaliYogaAssessmentResponse.From).ToArray(),
        value.Unsupported.Select(KundaliUnsupportedResponse.From).ToArray());
}

/// <summary>One assessed combination. <paramref name="Reason"/> is present only for <c>indeterminate</c>; a satisfied
/// or failed assessment is explained by its conditions.</summary>
public sealed record KundaliYogaAssessmentResponse(
    string Code,
    string Name,
    string Scope,
    string Status,
    IReadOnlyList<KundaliYogaConditionResponse> Conditions,
    IReadOnlyList<string> Supporting,
    IReadOnlyList<string> Opposing,
    IReadOnlyList<string> Exceptions,
    IReadOnlyList<string> Cancellations,
    bool Complete,
    string? Reason,
    IReadOnlyList<string> UntestedComponents)
{
    public static KundaliYogaAssessmentResponse From(MyVedicStar.Kundali.Shared.KundaliYogaAssessment value) => new(
        value.Code, value.Name, value.Scope, value.Status,
        value.Conditions.Select(KundaliYogaConditionResponse.From).ToArray(),
        value.Supporting, value.Opposing, value.Exceptions, value.Cancellations,
        value.Complete, value.Reason, value.UntestedComponents);
}

/// <summary>One condition, with the fact it was tested against.</summary>
public sealed record KundaliYogaConditionResponse(string Text, bool Truth)
{
    public static KundaliYogaConditionResponse From(MyVedicStar.Kundali.Shared.KundaliYogaCondition value) =>
        new(value.Text, value.Truth);
}

/// <summary>KUN-17 report request as it arrives. `Sections` is validated against the published closed set, so an
/// unknown id is a 400 rather than something silently dropped.</summary>
public sealed record KundaliReportRequestResponse(
    string Variant,
    string[] Sections,
    string? Locale)
{
    public MyVedicStar.Kundali.Shared.KundaliReportRequest ToRequest() =>
        new(Variant, Sections ?? [], Locale);
}

/// <summary>KUN-17 report state. `DownloadUrl` is owner-scoped and `DownloadExpiresAtUtc` is the EFFECTIVE expiry -
/// the minimum of the issue window and the report's own lifetime - so a client that checks the field and a server
/// that enforces validity agree by construction.</summary>
public sealed record KundaliReportResponse(
    Guid ReportId,
    Guid ProfileId,
    string Variant,
    string[] Sections,
    string Status,
    long Revision,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? DownloadUrl,
    DateTimeOffset? DownloadExpiresAtUtc,
    KundaliReportFailureResponse[] Failures)
{
    public static KundaliReportResponse From(MyVedicStar.Kundali.Shared.KundaliReport value) => new(
        value.ReportId, value.ProfileId, value.Variant, value.Sections.ToArray(), value.Status, value.Revision,
        value.RequestedAtUtc, value.CompletedAtUtc, value.DownloadUrl, value.DownloadExpiresAtUtc,
        value.Failures.Select(KundaliReportFailureResponse.From).ToArray());
}

/// <summary>Where a generation failure happened and what happened. `Code` is from the closed
/// `KundaliReportFailureCodes` set so a client can translate it; `Detail` is diagnostic text that is never
/// rendered to a user.</summary>
public sealed record KundaliReportFailureResponse(string Section, string Code, string? Detail)
{
    public static KundaliReportFailureResponse From(MyVedicStar.Kundali.Shared.KundaliReportFailure value) =>
        new(value.Section, value.Code, value.Detail);
}

/// <summary>KUN-16 remedies (api#128). Every item is bound to the finding it addresses, with that finding's status.
/// Only <c>evaluated</c> findings are ever emitted with a remedy; suppressed findings appear in <c>unsupported</c>
/// with a reason code, so an omission is attributable rather than silent.</summary>
public sealed record KundaliRemediesResponse(
    string CatalogVersion,
    IReadOnlyList<KundaliRemedyResponse> Items,
    IReadOnlyList<KundaliUnsupportedResponse> Unsupported)
{
    public static KundaliRemediesResponse From(MyVedicStar.Kundali.Shared.KundaliRemediesSection value) => new(
        value.CatalogVersion,
        value.Items.Select(KundaliRemedyResponse.From).ToArray(),
        value.Unsupported.Select(KundaliUnsupportedResponse.From).ToArray());
}

/// <summary>One remedy. <paramref name="FindingStatus"/> is the status the remedy was emitted under.</summary>
public sealed record KundaliRemedyResponse(
    string FindingCode,
    string FindingStatus,
    string Category,
    string Suggestion,
    string Tradition,
    string Reference)
{
    public static KundaliRemedyResponse From(MyVedicStar.Kundali.Shared.KundaliRemedy value) => new(
        value.FindingCode, value.FindingStatus, value.Category, value.Suggestion, value.Tradition, value.Reference);
}
