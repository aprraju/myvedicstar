using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Authorize.Business.Options;
using MyVedicStar.Authorize.Business.Users;
using MyVedicStar.Authorize.Data.DependencyInjection;
using MyVedicStar.Authorize.Integration.DependencyInjection;

namespace MyVedicStar.Authorize.Business.DependencyInjection;

public static class AuthorizeBusinessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Authorize facade and everything it depends on, so the API only needs to reference the Business layer.
    /// </summary>
    public static IServiceCollection AddAuthorizeBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<UserIdentityClaimOptions>()
            .Bind(configuration.GetSection(UserIdentityClaimOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthorizeIntegrations(configuration);

        // Decided at registration time: with persistence off, no database services, settings or migrations are registered.
        var featureFlags = configuration.GetSection(FeatureFlagOptions.SectionName).Get<FeatureFlagOptions>() ?? new FeatureFlagOptions();
        if (featureFlags.UserPersistence)
        {
            services.AddAuthorizeData(configuration);
            services.AddScoped<IUserSignInRecorder, DatabaseUserSignInRecorder>();
        }
        else
        {
            services.AddScoped<IUserSignInRecorder, DisabledUserSignInRecorder>();
        }

        services.AddSingleton<ExternalUserIdResolver>();
        services.AddScoped<UserSignInService>();
        services.AddScoped<IAuthorizeFacade, AuthorizeFacade>();

        return services;
    }
}
