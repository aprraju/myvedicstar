using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Calculation.Elements;
using MyVedicDay.Panchang.Calculation.Traditions;

namespace MyVedicDay.Panchang.Calculation;

/// <summary>
/// Registers the Panchang calculation engine services.
/// </summary>
public static class PanchangCalculationServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IPanchangCalculator"/>, <see cref="IPanchangTraditionCatalog"/> and their internal dependencies.
    /// All services are stateless singletons.
    /// </summary>
    public static IServiceCollection AddPanchangCalculation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAstronomy();
        services.TryAddSingleton<PanchangElementCalculator>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPanchangTraditionStrategy, DrikPanchangStrategy>());
        services.TryAddSingleton<PanchangTraditionStrategyProvider>();
        services.TryAddSingleton<IPanchangTraditionCatalog>(provider => provider.GetRequiredService<PanchangTraditionStrategyProvider>());

        services.TryAddSingleton<IPanchangCalculator, PanchangCalculator>();

        return services;
    }
}
