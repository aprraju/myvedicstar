using MyVedicDay.Panchang.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Ai;
using MyVedicDay.Caching;
using MyVedicDay.Localization;
using MyVedicDay.Location;
using MyVedicDay.Muhurta.Business.Activities;
using MyVedicDay.Muhurta.Business.Daily;
using MyVedicDay.Muhurta.Business.Explanation;
using MyVedicDay.Muhurta.Business.Rules;
using MyVedicDay.Muhurta.Calculation;
using MyVedicDay.Muhurta.Data;
using MyVedicDay.Traditions;
using NodaTime;

namespace MyVedicDay.Muhurta.Business;

/// <summary>
/// Registers the Muhurta (Find a Good Time) business layer and everything it builds on: its own layers and the
/// shared platform modules (Location, Localization, Traditions, Caching). The API depends only on
/// <see cref="IMuhurtaFacade"/>.
/// </summary>
public static class MuhurtaBusinessServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IMuhurtaFacade"/>. "Other" descriptions are mapped by the registered
    /// <see cref="IActivityInterpreter"/>s in order: AI first (when <c>Ai:Enabled</c>), then keywords.
    /// </summary>
    public static IServiceCollection AddMuhurtaBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MuhurtaOptions>()
            .Bind(configuration.GetSection(MuhurtaOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IClock>(SystemClock.Instance);

        services.TryAddSingleton<MuhurtaRulesProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityInterpreter, AiActivityInterpreter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityInterpreter, KeywordActivityInterpreter>());
        services.TryAddSingleton<GoodTimeExplainer>();
        services.TryAddSingleton<ActivityResolver>();
        services.TryAddSingleton<MuhurtaDayService>();
        services.TryAddScoped<IMuhurtaFacade, MuhurtaFacade>();

        // Muhurta layers.
        services.AddMuhurtaCalculation();
        services.AddMuhurtaData(configuration);

        // Shared platform modules.
        services.AddLocationBusiness(configuration);
        services.AddLocalizationBusiness(configuration);
        services.AddTraditionsBusiness(configuration);
        services.AddDistributedObjectCache(configuration);
        services.AddPanchangCalculationCache();
        services.AddAiIntegration(configuration);

        return services;
    }
}
