using MyVedicDay.Panchang.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MyVedicDay.Astronomy;
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
        services.TryAddSingleton<DailyPanchangService>();
        services.TryAddSingleton<PanchangLocalizer>();
        services.TryAddSingleton<GuidanceRulesProvider>();
        services.TryAddSingleton<IDailyGuidanceEvaluator, DailyGuidanceEvaluator>();

        services.TryAddScoped<IPanchangFacade, PanchangFacade>();
        services.TryAddScoped<GlossaryFacade>();
        services.TryAddScoped<IGlossaryFacade>(provider => provider.GetRequiredService<GlossaryFacade>());
        services.TryAddScoped<IPanchangTraditionFacade>(provider => provider.GetRequiredService<GlossaryFacade>());

        // Bind the astronomy port to the configured provider.
        //
        // The key is read here and handed to AddAstronomyProvider, which parses it strictly: an UNKNOWN value still
        // fails at composition rather than degrading to the analytical series. An ABSENT value resolves to the
        // documented default instead, because absence is a normal state - the Day and Star hosts load their own
        // layered configuration and neither ships this section today. Defaulting absence in one named place keeps
        // that explicit, and decision D2 keeps the analytical series the default until AST-12 (#77) flips it.
        services.AddAstronomyProvider(
            configuration[AstronomyProviderRegistration.ConfigurationKey] ?? ProviderSelectionParser.AnalyticalKey);
        services.AddPanchangCalculation();
        services.AddPanchangData(configuration);

        // Shared platform modules.
        services.AddLocationBusiness(configuration);
        services.AddLocalizationBusiness(configuration);
        services.AddTraditionsBusiness(configuration);
        services.AddDistributedObjectCache(configuration);
        services.AddPanchangCalculationCache();

        return services;
    }
}
