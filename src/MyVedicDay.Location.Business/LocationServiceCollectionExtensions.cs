using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Location.Integration;
using NodaTime;

namespace MyVedicDay.Location;

/// <summary>
/// Registers the shared location services: place search, coordinate normalization and time zone resolution.
/// </summary>
public static class LocationServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ILocationResolver"/>, <see cref="ILocationFacade"/> and the location provider adapters.
    /// Safe to call from several modules.
    /// </summary>
    public static IServiceCollection AddLocationBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ILocationResolver)))
        {
            return services;
        }

        services.AddOptions<LocationOptions>()
            .Bind(configuration.GetSection(LocationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IDateTimeZoneProvider>(DateTimeZoneProviders.Tzdb);
        services.TryAddSingleton<PlaceResolver>();
        services.TryAddSingleton<ILocationResolver, LocationResolver>();
        services.TryAddScoped<ILocationFacade, LocationFacade>();

        services.AddLocationIntegration(configuration);

        return services;
    }
}
