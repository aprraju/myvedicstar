using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Infrastructure.Data;
namespace MyVedicStar.Settings.Data;

public static class SettingsDataRegistration
{
    public static IServiceCollection AddSettingsData(this IServiceCollection services)
    {
        services.AddDbContext<SettingsDbContext>((provider, builder) => DatabaseConfiguration.ConfigureDomainDatabase(builder, provider, "myvedicstarsettings"));
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddHostedService<SettingsDatabaseMigrationHostedService>();
        return services;
    }
}
