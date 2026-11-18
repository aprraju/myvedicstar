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
    string[] UnsupportedCapabilities)
{
    public static KundaliCapabilitiesResponse From(MyVedicStar.Kundali.Shared.KundaliCapabilityReport value) => new(
        value.SupportedDivisionalCharts.ToArray(), value.ProvisionalDivisionalCharts.ToArray(),
        value.SupportedShadbalaComponents.ToArray(), value.UnsupportedShadbalaComponents.ToArray(),
        value.LunarNodeConvention, value.AscendantConvention, value.MinimumSupportedBirthDate,
        value.MaximumSupportedBirthDate, value.UnsupportedCapabilities.ToArray());
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

