using System.Security.Claims;
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
        services.TryAddSingleton(KundaliEngineVersions.Foundation);
        services.AddScoped<KundaliValidator>();
        services.AddScoped<IKundaliFacade, KundaliFacade>();

        // Composition supplies the real reader; this default keeps the facade constructible in isolation so a test
        // or host that does not wire astronomy still resolves. It reports the analytical provider's identity, which
        // is the documented default until AST-12 flips it, and never fabricates a version.
        services.TryAddScoped<IKundaliAstronomyMetadataReader, UnconfiguredAstronomyMetadataReader>();
        return services;
    }
}


/// <summary>Fallback used when composition has not supplied a provider-aware reader. Reports the documented default
/// analytical provider rather than an empty or invented value.</summary>
internal sealed class UnconfiguredAstronomyMetadataReader : IKundaliAstronomyMetadataReader
{
    public Task<KundaliAstronomyMetadata> GetAsync(ClaimsPrincipal user, CancellationToken token) =>
        Task.FromResult(new KundaliAstronomyMetadata("analytical", "analytical", "Lahiri", "WholeSign", "Mean",
            "Sidereal"));
}
