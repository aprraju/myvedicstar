using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

public static class KundaliBusinessRegistration
{
    /// <summary>Registers internal business implementations; composition supplies the profile-reader and repository
    /// ports. A previously registered TimeProvider and engine versions are retained for deterministic tests.</summary>
    public static IServiceCollection AddKundaliBusiness(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(KundaliEngineVersions.Foundation);
        services.AddScoped<KundaliValidator>();
        services.AddScoped<IKundaliFacade, KundaliFacade>();

        // Composition supplies the real reader; this default keeps the facade constructible in isolation so a test
        // or host that does not wire astronomy still resolves. It reports the analytical provider's identity, which
        // is the documented default until AST-12 flips it, and never fabricates a version.
        services.TryAddScoped<IKundaliAstronomyMetadataReader, UnconfiguredAstronomyMetadataReader>();

        // Composition supplies the real engine; this default keeps the facade constructible in isolation.
        services.TryAddScoped<IKundaliCalculationEngine, UnconfiguredKundaliCalculationEngine>();
        services.TryAddScoped<IKundaliDivisionalMapper, UnconfiguredDivisionalMapper>();
        services.TryAddScoped<IKundaliDignityRules, UnconfiguredDignityRules>();
        services.TryAddScoped<IKundaliAshtakavargaRules, UnconfiguredAshtakavargaRules>();
        services.TryAddScoped<IKundaliAshtakootaRules, UnconfiguredAshtakootaRules>();
        services.TryAddScoped<IKundaliShadbalaRules, UnconfiguredShadbalaRules>();
        services.TryAddScoped<IKundaliYogaRules, UnconfiguredYogaRules>();

        // api#54 (KUN-14): the provider-backed narrator is supplied by composition. The default narrates nothing and
        // reports ai-disabled, rather than returning invented prose: a host without a model must still serve every other
        // section, and a silent empty interpretation would be indistinguishable from a withheld one.
        services.TryAddScoped<IKundaliInterpretationNarrator, UnconfiguredInterpretationNarrator>();

        // Composition supplies the real alert store. The default refuses rather than answering "no consents", because
        // an empty list from a missing store is indistinguishable from a reader who opted out - and would silently
        // disable email they asked for.
        services.TryAddScoped<IKundaliAlertRepository, UnconfiguredAlertRepository>();

        // KUN-17 (api#57): both report ports default to a refusal, so a host that forgets to wire the store or the
        // renderer reports renderer-unavailable rather than producing a report that will never settle. Composition
        // replaces them; neither fallback can fabricate a document.
        services.TryAddScoped<IKundaliReportStore, UnconfiguredReportStore>();
        services.TryAddScoped<IKundaliReportRenderer, UnconfiguredReportRenderer>();
        return services;
    }
}


/// <summary>Fallback used when composition has not supplied a provider-aware reader. Reports the documented default
/// analytical provider rather than an empty or invented value.</summary>
internal sealed class UnconfiguredAstronomyMetadataReader : IKundaliAstronomyMetadataReader
{
    public Task<KundaliAstronomyMetadata> GetAsync(ClaimsPrincipal user, CancellationToken token) =>
        Task.FromResult(new KundaliAstronomyMetadata("analytical", "analytical", "Lahiri", "WholeSign", "Mean",
            "Sidereal"));
}

/// <summary>
/// Fallback used when composition has not supplied the real engine. Reports everything as not-implemented rather than
/// inventing values, so a host that forgets to wire the engine degrades visibly instead of silently producing signs.
/// </summary>

/// <summary>
/// Divisional sign mapping as a port. The rule lives in the calculation project; Business consumes the abstraction so
/// the layering (Business never references Calculation) holds and the mapper can be tested in isolation.
/// </summary>
public interface IKundaliDivisionalMapper
{
    /// <summary>True when the mapping rule for this division is implemented.</summary>
    bool IsImplemented(int divisional);

    /// <summary>Zero-based sign index of the longitude in the given divisional chart.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The division is not implemented.</exception>
    int SignIn(int divisional, double siderealLongitude);
}



/// <summary>
/// Dignity, combustion and aspect rules as a port. The rules live in the calculation project; Business consumes the
/// abstraction so the layering (Business never references Calculation) holds and the rules stay testable in isolation.
/// </summary>

/// <summary>
/// Ashtakavarga scoring as a port. The tables and arithmetic live in the calculation project; Business consumes the
/// abstraction so the layering holds.
/// </summary>
public interface IKundaliAshtakavargaRules
{
    /// <summary>Identifies the exact table set in use.</summary>
    string RulesVersion { get; }

    /// <summary>The seven scored planets.</summary>
    IReadOnlyList<string> ScoredPlanets { get; }

    /// <summary>Bhinnashtakavarga for one planet: twelve bindu counts indexed from Aries.</summary>
    IReadOnlyList<int> Bhinna(string planet, IReadOnlyDictionary<string, int> signByReference);

    /// <summary>Sarvashtakavarga: twelve sign totals derived from the bhinnas.</summary>
    IReadOnlyList<int> Sarva(IReadOnlyDictionary<string, int[]> bhinnaByPlanet);
}

/// <summary>
/// Ashtakoota scoring as a port (KUN-15). The eight-koota tables and arithmetic live in the calculation project;
/// Business consumes the abstraction so the layering holds.
/// </summary>
/// <remarks>
/// The port deliberately exposes NO threshold and NO verdict method. The minimum acceptable-match score is an owner
/// decision that is still open, and a port method for it would invite someone to implement one locally, which is
/// precisely the client-constant failure the KUN-15 decision forbids.
/// </remarks>
public interface IKundaliAshtakootaRules
{
    /// <summary>Identifies the exact koota tables in use. Part of the comparison's cache identity.</summary>
    string RulesVersion { get; }

    /// <summary>Assesses two lunar sides. Returns scores and reasons; never a pass/fail judgement.</summary>
    AshtakootaAssessment Assess(AshtakootaSide sideA, AshtakootaSide sideB);
}

/// <summary>One side's Moon inputs, as the engine consumes them.</summary>
/// <param name="SignIndex">Zero-based Moon sign, 0 = Aries.</param>
/// <param name="NakshatraIndex">Zero-based nakshatra, 0 = Ashwini.</param>
/// <param name="Available">False when the Moon is undefined; every other field is then meaningless.</param>
/// <param name="UnavailableReason">A reason code when <paramref name="Available"/> is false.</param>
public sealed record AshtakootaSide(int SignIndex, int NakshatraIndex, bool Available, string? UnavailableReason)
{
    public static AshtakootaSide Unavailable(string reason) => new(0, 0, false, reason);

    public static AshtakootaSide Of(int signIndex, int nakshatraIndex) => new(signIndex, nakshatraIndex, true, null);
}

/// <summary>One category result across the port boundary.</summary>
public sealed record AshtakootaCategoryResult(string Code, int Maximum, int? Score, string? Reason, string? Side,
    string Note)
{
    public bool IsScored => Score.HasValue;
}

/// <summary>The port-level assessment. No threshold field exists, by design.</summary>
public sealed record AshtakootaAssessment(
    string RulesVersion,
    IReadOnlyList<AshtakootaCategoryResult> Categories,
    int ScoredTotal,
    string Certainty);

/// <summary>Reason codes a koota category can carry. Mirrored from the calculation project so Business can name them.</summary>
public static class AshtakootaReasons
{
    public const string BirthTimeUnknown = "BirthTimeUnknown";
    public const string MoonPositionUnavailable = "MoonPositionUnavailable";
}

/// <summary>Certainty values, closed set.</summary>
public static class AshtakootaCertainty
{
    public const string Complete = "complete";
    public const string Partial = "partial";
}

/// <summary>The eight koota codes and the overall maximum, so Business can size a response without the tables.</summary>
public static class AshtakootaShape
{
    public const int MaximumTotal = 36;

    public static readonly IReadOnlyList<string> Codes =
        ["Varna", "Vashya", "Tara", "Yoni", "GrahaMaitri", "Gana", "Bhakoot", "Nadi"];
}

/// <summary>
/// Shadbala computation as a port. The component rules live in the calculation project; Business consumes the
/// abstraction so the layering holds and the rules stay testable without a host.
/// </summary>
public interface IKundaliShadbalaRules
{
    /// <summary>Identifies the exact component rules and threshold table in use.</summary>
    string RulesVersion { get; }

    /// <summary>The strength unit, echoed rather than assumed.</summary>
    string Unit { get; }

    /// <summary>The seven scored planets.</summary>
    IReadOnlyList<string> ScoredPlanets { get; }

    /// <summary>Classical components this build cannot produce, with a reason each.</summary>
    IReadOnlyDictionary<string, string> WithheldComponents { get; }

    /// <summary>Per-planet strength from sidereal longitudes.</summary>
    IReadOnlyList<KundaliShadbalaResult> Compute(IReadOnlyDictionary<string, double> longitudeByPlanet);
}

/// <summary>
/// Yoga and dosha evaluation as a port. The rule catalogue lives in the calculation project; Business consumes the
/// abstraction so the layering holds and the rules stay testable without a host.
/// </summary>
public interface IKundaliYogaRules
{
    /// <summary>Identifies the exact rule catalogue in use.</summary>
    string RulesVersion { get; }

    /// <summary>Verdict vocabulary, echoed so the API never invents its own labels.</summary>
    IReadOnlyList<string> Statuses { get; }

    /// <summary>Every code the catalogue can report, so a caller can enumerate what exists even when nothing is run.</summary>
    IReadOnlyList<string> Catalogue { get; }

    /// <summary>Evaluates the catalogue against published sign placements.</summary>
    KundaliYogaEvaluation Evaluate(IReadOnlyDictionary<string, int> signByGraha);
}

/// <summary>Evaluated and unevaluated assessments, kept apart but delivered together.</summary>
public sealed record KundaliYogaEvaluation(
    string RulesVersion,
    IReadOnlyList<KundaliYogaResult> Evaluated,
    IReadOnlyList<KundaliYogaResult> NotEvaluated);

/// <summary>One assessed combination.</summary>
public sealed record KundaliYogaResult(
    string Code,
    string Name,
    string Scope,
    string Status,
    IReadOnlyList<KundaliYogaConditionResult> Conditions,
    IReadOnlyList<string> Supporting,
    IReadOnlyList<string> Opposing,
    IReadOnlyList<string> Exceptions,
    IReadOnlyList<string> Cancellations,
    bool Complete,
    string? Reason,
    IReadOnlyList<string> UntestedComponents);

/// <summary>One condition, with the fact it was tested against.</summary>
public sealed record KundaliYogaConditionResult(string Text, bool Truth);

/// <summary>One planet's computed strength, with every component's basis attached.</summary>
public sealed record KundaliShadbalaResult(
    string Planet,
    IReadOnlyList<KundaliShadbalaComponent> Components,
    double TotalRupas,
    IReadOnlyList<string> ComponentsWithheld,
    double? RequiredRupas,
    double? RatioToThreshold,
    IReadOnlyList<string> ComponentsInTotal,
    IReadOnlyList<string> ComponentsExcludedFromTotal);

/// <summary>One component's value, the basis it was computed on, and its machine-readable state.</summary>
/// <remarks>State exists so a consumer can distinguish a computed zero from a placeholder zero without parsing
/// <paramref name="Basis"/>. Only <c>computed</c> components are strength.</remarks>
public sealed record KundaliShadbalaComponent(string Component, double Rupas, string Basis, string State);


public interface IKundaliDignityRules
{
    /// <summary>The published dignity vocabulary, strongest first.</summary>
    IReadOnlyList<string> DignityStates { get; }

    /// <summary>Dignity of a planet at a sidereal longitude.</summary>
    string DignityOf(string planet, double siderealLongitude);

    /// <summary>Sign indices this planet aspects from the given sign.</summary>
    IReadOnlyList<int> AspectedSigns(string planet, int fromSign);
}


internal sealed class UnconfiguredKundaliCalculationEngine : IKundaliCalculationEngine
{
    public KundaliEngineResult Calculate(KundaliEngineInput input) => new(null, null, null, null, null, null, null,
    [
        new KundaliEngineGap("SunSign", KundaliEngineReasons.NotImplemented),
        new KundaliEngineGap("MoonSign", KundaliEngineReasons.NotImplemented),
        new KundaliEngineGap("VimshottariMahadashas", KundaliEngineReasons.NotImplemented),
        new KundaliEngineGap("YoginiPeriods", KundaliEngineReasons.NotImplemented),
        new KundaliEngineGap("CharaSignPeriods", KundaliEngineReasons.NotImplemented)
    ]);

    public KundaliEngineDashaTimeline CalculateDashaTimeline(KundaliDashaQuery query) =>
        new KundaliEngineDashaTimeline(null, [], [], null, null, [new KundaliEngineGap("VimshottariMahadashas", KundaliEngineReasons.NotImplemented)]);

    /// <summary>
    /// No transits without an engine. The signs are reported as empty strings and every window list is empty with a
    /// gap, rather than a plausible-looking window: a fabricated ingress date is exactly the failure this fallback
    /// exists to make loud.
    /// </summary>
    public KundaliEngineTransits CalculateTransits(KundaliTransitQuery query) =>
        new(string.Empty, [], string.Empty, [], null,
        [
            new KundaliEngineGap("Saturn", KundaliEngineReasons.NotImplemented),
            new KundaliEngineGap("Jupiter", KundaliEngineReasons.NotImplemented),
            new KundaliEngineGap("SadeSati", KundaliEngineReasons.NotImplemented)
        ]);
}
/// <summary>Fallback used when composition has not supplied the real mapper. Reports nothing as implemented, so a
/// missing registration surfaces as an unsupported chart rather than a silently wrong one.</summary>
internal sealed class UnconfiguredDivisionalMapper : IKundaliDivisionalMapper
{
    public bool IsImplemented(int divisional) => false;

    public int SignIn(int divisional, double siderealLongitude) =>
        throw new ArgumentOutOfRangeException(nameof(divisional), divisional,
            "No divisional mapper is registered.");
}

/// <summary>Fallback used when composition has not supplied the real rules. Reports no dignity states and refuses
/// to classify, so a missing registration surfaces as unsupported rather than as a silently wrong dignity.</summary>

/// <summary>Fallback when composition has not supplied the real koota rules. Refuses rather than scoring.</summary>
internal sealed class UnconfiguredAshtakootaRules : IKundaliAshtakootaRules
{
    /// <summary>Throws for the same reason the scoring call does: the version reaches the cache identity first, so a
    /// placeholder string here would create a stored comparison under rules that do not exist.</summary>
    public string RulesVersion => throw new InvalidOperationException("No Ashtakoota rules are registered.");

    public AshtakootaAssessment Assess(AshtakootaSide sideA, AshtakootaSide sideB) =>
        throw new InvalidOperationException("No Ashtakoota rules are registered.");
}

/// <summary>Fallback used when composition has not supplied the real rules. Refuses to score rather than returning
/// zeroes, which would look like a valid empty chart.</summary>
internal sealed class UnconfiguredAshtakavargaRules : IKundaliAshtakavargaRules
{
    /// <summary>The version throws for the same reason the scoring calls do: the facade reads it on the ordinary path
    /// to build every result's revision key, so a version string here would reach identity before any scoring call
    /// refused. A missing registration must fail where the version is asked for.</summary>
    public string RulesVersion => throw new InvalidOperationException("No Ashtakavarga rules are registered.");

    public IReadOnlyList<string> ScoredPlanets => [];

    public IReadOnlyList<int> Bhinna(string planet, IReadOnlyDictionary<string, int> signByReference) =>
        throw new InvalidOperationException("No Ashtakavarga rules are registered.");

    public IReadOnlyList<int> Sarva(IReadOnlyDictionary<string, int[]> bhinnaByPlanet) =>
        throw new InvalidOperationException("No Ashtakavarga rules are registered.");
}


/// <summary>Fallback used when composition has not supplied the real rules. Reports nothing as producible and
/// refuses to compute, so a missing registration surfaces as an unsupported section rather than as a silently wrong
/// strength.</summary>
internal sealed class UnconfiguredShadbalaRules : IKundaliShadbalaRules
{
    /// <summary>Throws rather than naming a version: an unconfigured version reaching identity would be
    /// indistinguishable from any other unconfigured result.</summary>
    public string RulesVersion => throw new InvalidOperationException("No Shadbala rules are registered.");

    public string Unit => "rupas";

    public IReadOnlyList<string> ScoredPlanets => [];

    public IReadOnlyDictionary<string, string> WithheldComponents =>
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<KundaliShadbalaResult> Compute(IReadOnlyDictionary<string, double> longitudeByPlanet) =>
        throw new InvalidOperationException("No Shadbala rules are registered.");
}

/// <summary>Fallback used when composition has not supplied the real catalogue. Reports no rules and refuses to
/// evaluate, so a missing registration surfaces as an unsupported section rather than as an empty clean chart.</summary>
internal sealed class UnconfiguredYogaRules : IKundaliYogaRules
{
    /// <summary>Throws rather than naming a version: an unconfigured version reaching identity would be
    /// indistinguishable from any other unconfigured result.</summary>
    public string RulesVersion => throw new InvalidOperationException("No yoga rules are registered.");

    public IReadOnlyList<string> Statuses => [];

    public IReadOnlyList<string> Catalogue => [];

    public KundaliYogaEvaluation Evaluate(IReadOnlyDictionary<string, int> signByGraha) =>
        throw new InvalidOperationException("No yoga rules are registered.");
}

internal sealed class UnconfiguredDignityRules : IKundaliDignityRules
{
    public IReadOnlyList<string> DignityStates => [];

    public string DignityOf(string planet, double siderealLongitude) =>
        throw new InvalidOperationException("No dignity rules are registered.");

    public IReadOnlyList<int> AspectedSigns(string planet, int fromSign) => [];
}

/// <summary>
/// Fallback used when composition has not supplied an alert store. Every operation throws, so a host that forgets to
/// register the store fails loudly instead of reporting "no subscriptions" for a reader who has some.
/// </summary>
internal sealed class UnconfiguredAlertRepository : IKundaliAlertRepository
{
    private const string Message =
        "No Kundali alert store is registered. Compose the Data layer with AddKundaliData.";

    public Task<IReadOnlyList<KundaliAlertPreference>> GetAsync(Guid accountId, Guid profileId,
        CancellationToken token) => throw new InvalidOperationException(Message);

    public Task<IReadOnlyList<KundaliAlertPreference>> GrantAsync(Guid accountId, Guid profileId,
        IReadOnlyCollection<string> eventTypes, string channel, CancellationToken token) =>
        throw new InvalidOperationException(Message);

    public Task<int> RevokeAllAsync(Guid accountId, Guid profileId, CancellationToken token) =>
        throw new InvalidOperationException(Message);

    public Task<IReadOnlyList<KundaliAlertPreference>> GetForDeliveryAsync(Guid accountId, Guid profileId,
        CancellationToken token) => throw new InvalidOperationException(Message);
}

/// <summary>The narrator used when composition supplies no AI provider: it narrates nothing and says why.</summary>
internal sealed class UnconfiguredInterpretationNarrator : IKundaliInterpretationNarrator
{
    public bool IsEnabled => false;

    public string Fingerprint => "none";

    public string PromptVersion => "kundali-interpretation-unconfigured";

    public Task<InterpretationVerdict> NarrateAsync(string domain, string locale, long sourceRevision,
        IReadOnlyList<KundaliInterpretationFactor> factors, CancellationToken token) =>
        Task.FromResult(InterpretationRule.NotEvaluated(domain, KundaliInterpretationReasons.AiDisabled));
}

/// <summary>KUN-17 (api#57) fallback store. Every operation throws, so a host without the Data layer fails loudly at the
/// report route rather than creating a record nothing will ever settle.</summary>
internal sealed class UnconfiguredReportStore : IKundaliReportStore
{
    private const string Message =
        "No Kundali report store is registered. Compose the Data layer, or leave reports unsupported on this host.";

    public Task<KundaliReport> EnqueueAsync(Guid accountId, Guid profileId, KundaliReportRequest request,
        long sourceRevision, TimeProvider clock, CancellationToken token) => throw new NotSupportedException(Message);

    public Task<KundaliReport?> FindAsync(Guid accountId, Guid profileId, Guid reportId, CancellationToken token) =>
        throw new NotSupportedException(Message);

    public Task<KundaliReport> SettleAsync(Guid accountId, Guid profileId, Guid reportId, string status,
        string? downloadUrl, DateTimeOffset? downloadExpiresAtUtc, IReadOnlyList<KundaliReportFailure> failures,
        TimeProvider clock, CancellationToken token) => throw new NotSupportedException(Message);
}

/// <summary>KUN-17 (api#57) fallback renderer. Reports itself unavailable rather than pretending to render: the facade
/// then settles the report as <c>renderer-unavailable</c>, which is a fact a caller can act on, instead of a document
/// that never arrives.</summary>
internal sealed class UnconfiguredReportRenderer : IKundaliReportRenderer
{
    public bool IsAvailable => false;

    public Task<KundaliReportRenderResult> RenderAsync(Guid accountId, Guid profileId, KundaliReportRequest request,
        long sourceRevision, CancellationToken token) =>
        throw new NotSupportedException("No Kundali report renderer is registered.");
}
