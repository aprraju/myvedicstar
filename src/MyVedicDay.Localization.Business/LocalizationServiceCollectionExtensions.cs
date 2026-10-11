using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MyVedicDay.Localization.Data;

namespace MyVedicDay.Localization;

/// <summary>
/// Registers the shared localization services: response-language negotiation, content with fallback, and
/// location-based date/time display formatting.
/// </summary>
public static class LocalizationServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ILocalizedContentProvider"/> and <see cref="IDateTimeDisplayFormatter"/> with their options
    /// (<c>Localization</c>, <c>DateTimeDisplay</c>, <c>Content</c>). Safe to call from several modules.
    /// </summary>
    public static IServiceCollection AddLocalizationBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ILocalizedContentProvider)))
        {
            return services;
        }

        services.AddOptions<LocalizationOptions>()
            .Bind(configuration.GetSection(LocalizationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<LocalizationOptions>, LocalizationOptionsValidator>());

        services.AddOptions<DateTimeDisplayOptions>()
            .Bind(configuration.GetSection(DateTimeDisplayOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DateTimeDisplayOptions>, DateTimeDisplayOptionsValidator>());

        services.TryAddSingleton<LanguageResolver>();
        services.TryAddSingleton<ILocalizedContentProvider, LocalizedContentProvider>();
        services.TryAddSingleton<IDateTimeDisplayFormatter, DateTimeDisplayFormatter>();

        services.AddContentRepository(configuration);

        return services;
    }
}
