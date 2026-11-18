using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Integration;

/// <summary>
/// Adapts the pure divisional mapper to the business port. No logic of its own: the rule stays in one place and
/// Business sees only the abstraction.
/// </summary>
internal sealed class KundaliDivisionalMapperAdapter : IKundaliDivisionalMapper
{
    public bool IsImplemented(int divisional) => DivisionalCharts.IsImplemented(divisional);

    public int SignIn(int divisional, double siderealLongitude) =>
        DivisionalCharts.SignIn(divisional, siderealLongitude);
}

public static class KundaliDivisionalMapperRegistration
{
    /// <summary>Replaces the business fallback with the real mapper.</summary>
    public static IServiceCollection AddKundaliDivisionalMapper(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Scoped<IKundaliDivisionalMapper, KundaliDivisionalMapperAdapter>());
        return services;
    }
}

