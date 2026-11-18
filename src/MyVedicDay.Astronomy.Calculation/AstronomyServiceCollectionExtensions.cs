using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Astronomy.Core;

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
    /// <remarks>
    /// Kept for callers that do not configure a provider; it selects the analytical provider explicitly by name
    /// rather than registering <see cref="MeeusEphemeris"/> directly, so there is exactly one place that decides
    /// which provider sits behind the port. Callers that honour configuration should use
    /// <see cref="AstronomyProviderRegistration.AddAstronomyProvider"/> instead.
    /// </remarks>
    public static IServiceCollection AddAstronomy(this IServiceCollection services) =>
        services.AddAstronomy(ProviderSelection.Analytical);

    /// <summary>
    /// Adds the shared astronomy services with the port bound to the given provider selection.
    /// </summary>
    public static IServiceCollection AddAstronomy(this IServiceCollection services,
        ProviderSelection selection, Func<IServiceProvider, IEphemerisEngine>? configureSpice = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The port is decided in exactly one place. Registering MeeusEphemeris here as well would make the
        // configured selection dependent on call order, and Try* hides that kind of mistake.
        services.AddAstronomyProvider(ProviderSelectionParser.ToKey(selection), configureSpice);
        services.TryAddSingleton<ISunEventCalculator, SunEventCalculator>();
        services.TryAddSingleton<IMoonEventCalculator, MoonEventCalculator>();
        services.TryAddSingleton<IPlanetEphemeris, KeplerianPlanetEphemeris>();
        // The graha port is selected with the Sun/Moon port (one configuration key), never independently.
        services.TryAddSingleton<GrahaPositionsResolver>();
        services.TryAddSingleton<LahiriAyanamsa>();
        // The interface as well as the concrete type: consumers that depend on the abstraction (the Kundali engine
        // does) cannot resolve LahiriAyanamsa by its interface unless it is registered as one. Registering only the
        // concrete type made IAyanamsa unresolvable everywhere, which surfaced as a null rather than a clear
        // failure because GetService returns null for an unregistered service.
        services.TryAddSingleton<IAyanamsa>(provider => provider.GetRequiredService<LahiriAyanamsa>());
        services.TryAddSingleton<ILunisolarCalendar, LunisolarCalendar>();

        return services;
    }
}
