using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MyVedicDay.Caching;
using MyVedicDay.Localization;
using MyVedicDay.Location;
using MyVedicDay.Panchang.Business.Caching;
using MyVedicDay.Panchang.Business.Calendar;
using MyVedicDay.Panchang.Business.Configuration;
using MyVedicDay.Panchang.Business.Daily;
using MyVedicDay.Panchang.Business.Diagnostics;
using MyVedicDay.Panchang.Business.Guidance;
using MyVedicDay.Panchang.Business.Localization;
using MyVedicDay.Panchang.Business.Traditions;
using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Data;
using MyVedicDay.Traditions;
using NodaTime;

namespace MyVedicDay.Panchang.Business;

/// <summary>
/// Registers the Panchang business layer and everything it builds on.
/// </summary>
/// <remarks>
/// Composes Panchang's own lower layers (Calculation, Data) and the shared platform modules (Location, Localization,
/// Traditions, Caching). Platform registrations are idempotent, so other domains (Muhurta, Kundali) can register them
/// too. The API depends only on the facades.
/// </remarks>
public static class PanchangBusinessServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IPanchangFacade"/>, <see cref="IGlossaryFacade"/> and <see cref="IPanchangTraditionFacade"/> with
    /// their dependencies. Requires an <see cref="System.Diagnostics.Metrics.IMeterFactory"/> and logging, which the
    /// ASP.NET Core host provides.
    /// </summary>
    public static IServiceCollection AddPanchangBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PanchangOptions>()
            .Bind(configuration.GetSection(PanchangOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PanchangOptions>, PanchangOptionsValidator>());

        services.TryAddSingleton<IClock>(SystemClock.Instance);

        services.TryAddSingleton<PanchangMetrics>();
        services.TryAddSingleton<PanchangTraditionResolver>();
        services.TryAddSingleton<CalendarRangeBuilder>();
        services.TryAddSingleton<PanchangCacheKeyFactory>();
        services.TryAddSingleton<PanchangCacheExpirationPolicy>();
        services.TryAddSingleton<InFlightRequestCoalescer<DailyPanchangData>>();
        services.TryAddSingleton<DailyPanchangService>();
        services.TryAddSingleton<PanchangLocalizer>();
        services.TryAddSingleton<GuidanceRulesProvider>();
        services.TryAddSingleton<IDailyGuidanceEvaluator, DailyGuidanceEvaluator>();

        services.TryAddScoped<IPanchangFacade, PanchangFacade>();
        services.TryAddScoped<GlossaryFacade>();
        services.TryAddScoped<IGlossaryFacade>(provider => provider.GetRequiredService<GlossaryFacade>());
        services.TryAddScoped<IPanchangTraditionFacade>(provider => provider.GetRequiredService<GlossaryFacade>());

        // Panchang's own layers.
        services.AddPanchangCalculation();
        services.AddPanchangData(configuration);

        // Shared platform modules.
        services.AddLocationBusiness(configuration);
        services.AddLocalizationBusiness(configuration);
        services.AddTraditionsBusiness(configuration);
        services.AddDistributedObjectCache(configuration);

        return services;
    }
}
