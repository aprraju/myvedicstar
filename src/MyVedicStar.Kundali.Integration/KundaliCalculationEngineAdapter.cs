using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Calculation;
using NodaTime;

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

        var result = _engine.Calculate(ToRequest(input));

        // Reason codes are passed through rather than re-mapped: the engine's vocabulary is the source of truth, and a
        // translation layer here would silently drift from it.
        return new KundaliEngineResult(result.SiderealSunLongitude, result.SiderealMoonLongitude, result.SunSign,
            result.MoonSign,
            result.VimshottariMahadashas?
                .Select(period => new KundaliEngineDasha(period.Lord, period.StartUtc, period.EndUtc,
                    period.IsRemainder))
                .ToArray(),
            result.YoginiPeriods?
                .Select(period => new KundaliEngineYoginiPeriod(period.Name, period.Ruler, period.StartUtc,
                    period.EndUtc, period.IsRemainder))
                .ToArray(),
            result.CharaSignPeriods?
                .Select(period => new KundaliEngineCharaPeriod(period.Sign, period.StartUtc, period.EndUtc))
                .ToArray(),
            result.Gaps.Select(gap => new KundaliEngineGap(gap.Path, gap.Reason)).ToArray());
    }

    public KundaliEngineDashaTimeline CalculateDashaTimeline(KundaliDashaQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = _engine.Calculate(ToRequest(query.Input));

        if (result.VimshottariMahadashas is not { Count: > 0 } periods)
        {
            // The engine withheld the sequence and already explained why; carry its reasons through unchanged.
            return new KundaliEngineDashaTimeline(result.SiderealMoonLongitude, [], [], null, null,
                result.Gaps.Select(gap => new KundaliEngineGap(gap.Path, gap.Reason)).ToArray());
        }

        var mahadashas = periods
            .Select(period => new KundaliEngineDasha(period.Lord, period.StartUtc, period.EndUtc, period.IsRemainder))
            .ToArray();

        var at = query.At ?? throw new ArgumentNullException(nameof(query),
            "The caller must supply the query instant; the adapter never reads a clock.");

        var path = VimshottariSubPeriods.PathAt(
                periods.Select(period => new VimshottariPeriod(period.Lord,
                    Instant.FromDateTimeOffset(period.StartUtc), Instant.FromDateTimeOffset(period.EndUtc),
                    period.IsRemainder)).ToArray(),
                Instant.FromDateTimeOffset(at))
            .Select((sub, index) => new KundaliEngineDashaLevel(sub.Lord, LevelNames[index],
                sub.StartUtc.ToDateTimeOffset(), sub.EndUtc.ToDateTimeOffset()))
            .ToArray();

        return new KundaliEngineDashaTimeline(result.SiderealMoonLongitude, mahadashas, path, null, null,
            result.Gaps.Select(gap => new KundaliEngineGap(gap.Path, gap.Reason)).ToArray());
    }

    /// <summary>Path position to level name. Indexed because the engine returns the chain outermost first.</summary>
    private static readonly string[] LevelNames = ["Mahadasha", "Antara", "Pratyantar"];

    /// <summary>Single mapping from the port type to the engine request. Kept in one place so a new request
    /// field cannot be threaded through one path and forgotten in another.</summary>
    private static KundaliCalculationRequest ToRequest(KundaliEngineInput input) => new(input.BirthDate,
        input.BirthTimeMode, input.BirthTime, input.Latitude, input.Longitude, input.TimezoneId, input.Ayanamsa);
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
        services.AddKundaliDivisionalMapper();
        services.AddKundaliDignityRules();
        return services;
    }
}