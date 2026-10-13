using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Authorize.Data.DependencyInjection;
namespace MyVedicStar.Settings.Data;

public static class SettingsDataRegistration
{
    public static IServiceCollection AddSettingsData(this IServiceCollection services)
    {
        services.AddDbContext<SettingsDbContext>((provider, builder) => AuthorizeDataServiceCollectionExtensions.ConfigureDomainDatabase(builder, provider, "myvedicstarsettings"));
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        return services;
    }
}
