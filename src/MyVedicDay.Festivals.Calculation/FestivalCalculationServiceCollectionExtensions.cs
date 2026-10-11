using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Calculation;

namespace MyVedicDay.Festivals.Calculation;

/// <summary>
/// Registers the festival calculation engine.
/// </summary>
public static class FestivalCalculationServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IFestivalCalculator"/> and the Panchang calculation contract it builds on. Stateless singletons;
    /// safe to call from several modules.
    /// </summary>
    public static IServiceCollection AddFestivalCalculation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAstronomy();
        services.AddPanchangCalculation();
        services.TryAddSingleton<IFestivalCalculator, FestivalCalculator>();

        return services;
    }
}
