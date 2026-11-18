using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Integration;

/// <summary>
/// Adapts the pure calculation engine to the business port. Holds no logic of its own: it maps types and delegates,
/// so a change in the engine cannot be accidentally absorbed here.
/// </summary>
internal sealed class KundaliCalculationEngineAdapter : IKundaliCalculationEngine
{
    private readonly IKundaliEngine _engine;

    public KundaliCalculationEngineAdapter(IKundaliEngine engine) => _engine = engine;

    public KundaliEngineResult Calculate(KundaliEngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var request = new KundaliCalculationRequest(input.BirthDate, input.BirthTimeMode, input.BirthTime,
            input.Latitude, input.Longitude, input.TimezoneId, input.Ayanamsa);

        var result = _engine.Calculate(request);

        // Reason codes are passed through rather than re-mapped: the engine's vocabulary is the source of truth, and a
        // translation layer here would silently drift from it.
        return new KundaliEngineResult(result.SiderealSunLongitude, result.SiderealMoonLongitude, result.SunSign,
            result.MoonSign,
            result.VimshottariMahadashas?
                .Select(period => new KundaliEngineDasha(period.Lord, period.StartUtc, period.EndUtc,
                    period.IsRemainder))
                .ToArray(),
            result.Gaps.Select(gap => new KundaliEngineGap(gap.Path, gap.Reason)).ToArray());
    }
}


public static class KundaliCalculationEngineRegistration
{
    /// <summary>
    /// Replaces the business-layer fallback with the real engine. The engine is constructed from the host's
    /// IEphemeris and IAyanamsa, so this project never creates a provider of its own.
    /// </summary>
    public static IServiceCollection AddKundaliCalculationEngine(this IServiceCollection services)
    {
        services.AddScoped<Calculation.IKundaliEngine>(provider => new Calculation.KundaliEngine(
            provider.GetRequiredService<MyVedicDay.Astronomy.IEphemeris>(),
            provider.GetRequiredService<MyVedicDay.Astronomy.IAyanamsa>()));
        services.Replace(ServiceDescriptor.Scoped<IKundaliCalculationEngine, KundaliCalculationEngineAdapter>());
        return services;
    }
}