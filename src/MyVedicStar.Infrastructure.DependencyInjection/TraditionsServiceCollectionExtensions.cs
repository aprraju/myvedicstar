using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Localization;
using MyVedicDay.Localization.Data;

namespace MyVedicDay.Traditions;

/// <summary>
/// Tradition settings. Bound from the <c>Traditions</c> configuration section.
/// </summary>
/// <summary>
/// Registers the shared tradition list.
/// </summary>
public static class TraditionsServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ITraditionRegistry"/> and <see cref="ITraditionFacade"/>, and contributes the tradition names to the
    /// shared content. Safe to call from several modules.
    /// </summary>
    public static IServiceCollection AddTraditionsBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ITraditionRegistry)))
        {
            return services;
        }

        services.AddOptions<TraditionsOptions>()
            .Bind(configuration.GetSection(TraditionsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<ITraditionRegistry, TraditionRegistry>();
        services.TryAddScoped<ITraditionFacade, TraditionFacade>();

        services.AddLocalizationBusiness(configuration);
        services.AddContentSource(typeof(TraditionsOptions).Assembly);

        return services;
    }
}
