using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Localization.Data;

namespace MyVedicDay.Festivals.Data;

/// <summary>
/// Registers the festival data: the festival rules and the festival display content (contributed to the shared content
/// repository).
/// </summary>
public static class FestivalDataServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IFestivalRulesRepository"/> and registers this assembly's <c>festivals-content.*.json</c> catalogs
    /// with the shared content repository.
    /// </summary>
    public static IServiceCollection AddFestivalData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddContentRepository(configuration);
        services.AddContentSource(typeof(FestivalDataServiceCollectionExtensions).Assembly);
        services.TryAddSingleton<IFestivalRulesRepository, JsonFestivalRulesRepository>();

        return services;
    }
}
