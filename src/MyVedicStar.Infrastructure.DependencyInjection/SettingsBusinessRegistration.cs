using Microsoft.Extensions.Configuration;
using MyVedicStar.Authorize.Business.Users;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Settings.Data;
using MyVedicStar.Settings.Integration;
using MyVedicStar.Authorize.Business.Options;
using MyVedicDay.Traditions;
namespace MyVedicStar.Settings.Business;

public static class SettingsBusinessRegistration
{
    public static IServiceCollection AddSettingsBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        if (!(configuration.GetSection(FeatureFlagOptions.SectionName).Get<FeatureFlagOptions>()?.UserPersistence ?? false))
        { services.AddScoped<ISettingsFacade, DisabledSettingsFacade>(); return services; }
        services.AddSettingsData(); services.AddSettingsIntegration(); services.AddTraditionsBusiness(configuration);
        services.AddScoped<IAccountSignInHandler, AccountSettingsSignInHandler>();
        services.AddScoped<ISettingsFacade, SettingsFacade>(); return services;
    }
}
