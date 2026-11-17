using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MyVedicStar.Kundali.Business;

public static class KundaliBusinessRegistration
{
    /// <summary>Registers internal business implementations; composition supplies the profile-reader and repository
    /// ports. A previously registered TimeProvider and engine versions are retained for deterministic tests.</summary>
    public static IServiceCollection AddKundaliBusiness(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(KundaliEngineVersions.Defaults.Value);
        services.AddScoped<KundaliValidator>();
        services.AddScoped<IKundaliFacade, KundaliFacade>();
        return services;
    }
}
