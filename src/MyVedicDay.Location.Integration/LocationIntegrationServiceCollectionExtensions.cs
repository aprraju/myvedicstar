using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Location.Integration.GeoNames;
using MyVedicDay.Location.Integration.GeoTimeZone;

namespace MyVedicDay.Location.Integration;

/// <summary>
/// Registers the location provider adapters (geocoding, coordinate → time zone).
/// </summary>
public static class LocationIntegrationServiceCollectionExtensions
{
    /// <summary>
    /// Adds the offline GeoNames geocoder and GeoTimeZone lookup. <paramref name="configuration"/> is reserved for
    /// online provider settings (Azure Maps, Google) in later phases.
    /// </summary>
    public static IServiceCollection AddLocationIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<ITimeZoneLookupProvider, GeoTimeZoneLookupProvider>();
        services.TryAddSingleton<IGeocodingProvider, GeoNamesGeocodingProvider>();

        return services;
    }
}
