using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Integration;

/// <summary>Adapts the pure Shadbala rules to the business port. No logic of its own - every decision belongs in the
/// calculation project, where it is testable without a host.</summary>
internal sealed class KundaliShadbalaRulesAdapter : IKundaliShadbalaRules
{
    public string RulesVersion => Shadbala.RulesVersion;

    public string Unit => "rupas";

    public IReadOnlyList<string> ScoredPlanets => Shadbala.ScoredPlanets;

    public IReadOnlyDictionary<string, string> WithheldComponents => Shadbala.WithheldComponents;

    public IReadOnlyList<KundaliShadbalaResult> Compute(
        IReadOnlyDictionary<string, double> longitudeByPlanet) =>
        Shadbala.Compute(longitudeByPlanet).Planets
            .Select(planet => new KundaliShadbalaResult(
                planet.Planet,
                planet.Components
                    .Select(component => new KundaliShadbalaComponent(
                        component.Component, component.Rupas, component.Basis))
                    .ToArray(),
                planet.TotalRupas,
                planet.ComponentsWithheld,
                planet.RequiredRupas,
                planet.RatioToThreshold))
            .ToArray();
}

public static class KundaliShadbalaRulesRegistration
{
    public static IServiceCollection AddKundaliShadbalaRules(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Scoped<IKundaliShadbalaRules, KundaliShadbalaRulesAdapter>());
        return services;
    }
}
