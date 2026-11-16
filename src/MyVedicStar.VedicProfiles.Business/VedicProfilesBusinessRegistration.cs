using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MyVedicStar.VedicProfiles.Business;

public static class VedicProfilesBusinessRegistration
{
    /// <summary>Registers internal business implementations; composition supplies the repository/account ports.
    /// A previously registered TimeProvider is retained for deterministic tests.</summary>
    public static IServiceCollection AddVedicProfilesBusiness(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<VedicProfileValidator>();
        services.AddScoped<IVedicProfilesFacade, VedicProfilesFacade>();
        return services;
    }
}
