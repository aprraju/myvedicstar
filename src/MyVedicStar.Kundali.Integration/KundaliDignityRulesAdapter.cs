using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Integration;

/// <summary>
/// Adapts the pure dignity/aspect rules to the business port. No logic of its own: the rules stay in one place.
/// </summary>
internal sealed class KundaliDignityRulesAdapter : IKundaliDignityRules
{
    public IReadOnlyList<string> DignityStates => PlanetaryDignity.Dignities;

    public string DignityOf(string planet, double siderealLongitude) =>
        PlanetaryDignity.Of(planet, siderealLongitude);

    public IReadOnlyList<int> AspectedSigns(string planet, int fromSign) =>
        PlanetaryDignity.AspectedSigns(planet, fromSign);
}

public static class KundaliDignityRulesRegistration
{
    /// <summary>Replaces the business fallback with the real rules.</summary>
    public static IServiceCollection AddKundaliDignityRules(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Scoped<IKundaliDignityRules, KundaliDignityRulesAdapter>());
        return services;
    }
}

