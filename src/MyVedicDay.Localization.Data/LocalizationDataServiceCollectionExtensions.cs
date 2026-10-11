using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MyVedicDay.Localization.Data;

/// <summary>
/// Registers the shared content repository and the modules that contribute content to it.
/// </summary>
public static class LocalizationDataServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IContentRepository"/> and binds <see cref="ContentOptions"/>. Safe to call from several modules.
    /// </summary>
    public static IServiceCollection AddContentRepository(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(IContentRepository)))
        {
            return services;
        }

        services.AddOptions<ContentOptions>().Bind(configuration.GetSection(ContentOptions.SectionName));
        services.TryAddSingleton<IContentRepository, JsonContentRepository>();
        return services;
    }

    /// <summary>
    /// Registers an assembly whose embedded <c>MyVedicDay.Content.*.json</c> catalogs join the shared content.
    /// </summary>
    public static IServiceCollection AddContentSource(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        var alreadyRegistered = services.Any(descriptor =>
            descriptor.ServiceType == typeof(ContentSource)
            && descriptor.ImplementationInstance is ContentSource source
            && source.Assembly == assembly);

        if (!alreadyRegistered)
        {
            services.AddSingleton(new ContentSource(assembly));
        }

        return services;
    }
}
