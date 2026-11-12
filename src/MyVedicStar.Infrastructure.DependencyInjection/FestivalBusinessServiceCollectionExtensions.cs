using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Caching;
using MyVedicDay.Festivals.Business.Daily;
using MyVedicDay.Festivals.Business.Rules;
using MyVedicDay.Festivals.Calculation;
using MyVedicDay.Festivals.Data;
using MyVedicDay.Localization;
using MyVedicDay.Location;
using MyVedicDay.Traditions;
using NodaTime;

namespace MyVedicDay.Festivals.Business;

/// <summary>
/// Registers the festival business layer and everything it builds on.
/// </summary>
/// <remarks>
/// Composes the festival layers (Calculation, Data) and the shared platform modules (Location, Localization, Traditions,
/// Caching). Platform registrations are idempotent, so it can be registered alongside Panchang. The API depends only on
/// <see cref="IFestivalFacade"/>.
/// </remarks>
public static class FestivalBusinessServiceCollectionExtensions
{
    /// <summary>Adds <see cref="IFestivalFacade"/> with its dependencies.</summary>
    public static IServiceCollection AddFestivalBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<FestivalOptions>()
            .Bind(configuration.GetSection(FestivalOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IClock>(SystemClock.Instance);

        services.TryAddSingleton<FestivalRulesProvider>();
        services.TryAddSingleton<FestivalCacheKeyFactory>();
        services.TryAddSingleton<InFlightRequestCoalescer<IReadOnlyList<FestivalDayData>>>();
        services.TryAddSingleton<FestivalDayService>();
        services.TryAddScoped<IFestivalFacade, FestivalFacade>();

        // Festival layers.
        services.AddFestivalCalculation();
        services.AddFestivalData(configuration);

        // Shared platform modules.
        services.AddLocationBusiness(configuration);
        services.AddLocalizationBusiness(configuration);
        services.AddTraditionsBusiness(configuration);
        services.AddDistributedObjectCache(configuration);

        return services;
    }
}
