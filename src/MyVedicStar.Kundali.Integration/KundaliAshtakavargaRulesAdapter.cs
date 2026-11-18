using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Integration;

/// <summary>Adapts the pure Ashtakavarga rules to the business port. No logic of its own.</summary>
internal sealed class KundaliAshtakavargaRulesAdapter : IKundaliAshtakavargaRules
{
    public string RulesVersion => Ashtakavarga.RulesVersion;

    public IReadOnlyList<string> ScoredPlanets => Ashtakavarga.ScoredPlanets;

    public IReadOnlyList<int> Bhinna(string planet, IReadOnlyDictionary<string, int> signByReference) =>
        Ashtakavarga.Bhinna(planet, signByReference);

    public IReadOnlyList<int> Sarva(IReadOnlyDictionary<string, int[]> bhinnaByPlanet) =>
        Ashtakavarga.Sarva(bhinnaByPlanet);
}

public static class KundaliAshtakavargaRulesRegistration
{
    public static IServiceCollection AddKundaliAshtakavargaRules(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Scoped<IKundaliAshtakavargaRules, KundaliAshtakavargaRulesAdapter>());
        return services;
    }
}

