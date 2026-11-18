using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.TryAddScoped<IKundaliShadbalaRules, UnconfiguredShadbalaRules>();
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

/// <summary>Fallback used when composition has not supplied the real rules. Refuses to score rather than returning
/// zeroes, which would look like a valid empty chart.</summary>
internal sealed class UnconfiguredAshtakavargaRules : IKundaliAshtakavargaRules
{
    public string RulesVersion => "unconfigured";

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
    public string RulesVersion => "unconfigured";

    public string Unit => "rupas";

    public IReadOnlyList<string> ScoredPlanets => [];

    public IReadOnlyDictionary<string, string> WithheldComponents =>
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<KundaliShadbalaResult> Compute(IReadOnlyDictionary<string, double> longitudeByPlanet) =>
        throw new InvalidOperationException("No Shadbala rules are registered.");
}

internal sealed class UnconfiguredDignityRules : IKundaliDignityRules
{
    public IReadOnlyList<string> DignityStates => [];

    public string DignityOf(string planet, double siderealLongitude) =>
        throw new InvalidOperationException("No dignity rules are registered.");

    public IReadOnlyList<int> AspectedSigns(string planet, int fromSign) => [];
}
