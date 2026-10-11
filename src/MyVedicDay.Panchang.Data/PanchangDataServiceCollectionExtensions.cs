using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Localization.Data;
using MyVedicDay.Panchang.Data.Content;
using MyVedicDay.Panchang.Data.Guidance;

namespace MyVedicDay.Panchang.Data;

/// <summary>
/// Registers Panchang's data: its display content (contributed to the shared content repository), its tradition
/// profiles and its daily-guidance rules.
/// </summary>
public static class PanchangDataServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ITraditionProfileRepository"/> and <see cref="IGuidanceRulesRepository"/>, and registers this assembly's <c>panchang-content.*.json</c>
    /// catalogs with the shared content repository.
    /// </summary>
    public static IServiceCollection AddPanchangData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddContentRepository(configuration);
        services.AddContentSource(typeof(PanchangDataServiceCollectionExtensions).Assembly);
        services.TryAddSingleton<ITraditionProfileRepository, JsonTraditionProfileRepository>();
        services.TryAddSingleton<IGuidanceRulesRepository, JsonGuidanceRulesRepository>();

        return services;
    }
}
