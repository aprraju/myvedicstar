using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Registers the shared astronomy services used by every calculation domain (Panchang, Muhurta, Kundali…).
/// </summary>
public static class AstronomyServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IEphemeris"/>, <see cref="ISunEventCalculator"/>, <see cref="IMoonEventCalculator"/>, <see cref="IPlanetEphemeris"/>, <see cref="LahiriAyanamsa"/> and
    /// <see cref="ILunisolarCalendar"/>.
    /// Stateless singletons; safe to call from several modules.
    /// </summary>
    public static IServiceCollection AddAstronomy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IEphemeris, MeeusEphemeris>();
        services.TryAddSingleton<ISunEventCalculator, SunEventCalculator>();
        services.TryAddSingleton<IMoonEventCalculator, MoonEventCalculator>();
        services.TryAddSingleton<IPlanetEphemeris, KeplerianPlanetEphemeris>();
        services.TryAddSingleton<LahiriAyanamsa>();
        services.TryAddSingleton<ILunisolarCalendar, LunisolarCalendar>();

        return services;
    }
}
