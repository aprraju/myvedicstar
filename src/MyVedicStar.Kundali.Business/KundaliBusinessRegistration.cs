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
