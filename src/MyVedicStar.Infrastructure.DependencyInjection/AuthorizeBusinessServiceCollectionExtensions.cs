using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Authorize.Business.Options;
using MyVedicStar.Authorize.Business.Users;
using MyVedicStar.Authorize.Data.DependencyInjection;
using MyVedicStar.Shared.Caching;
using MyVedicStar.Authorize.Integration;

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

        services.AddMyVedicStarCaching(configuration);

        // Decided at registration time: with persistence off, no database services, settings or migrations are registered.
        var featureFlags = configuration.GetSection(FeatureFlagOptions.SectionName).Get<FeatureFlagOptions>() ?? new FeatureFlagOptions();
        if (featureFlags.UserPersistence)
        {
            services.AddOptions<AccountInitializationOptions>()
                .Bind(configuration.GetSection(AccountInitializationOptions.SectionName))
                .ValidateDataAnnotations().ValidateOnStart();
            services.AddAuthorizeData(configuration);
            services.AddScoped<IUserSignInRecorder, DatabaseUserSignInRecorder>();
        }
        else
        {
            services.AddScoped<IUserSignInRecorder, DisabledUserSignInRecorder>();
            services.AddScoped<MyVedicStar.Authorize.Data.Users.IProfileRepository, DisabledProfileRepository>();
        }

        services.AddSingleton<SignedInUserReader>();
        services.AddScoped<UserSignInService>();
        services.AddScoped<IAuthorizeFacade, AuthorizeFacade>();
        services.AddGraphProfileIntegration(configuration);
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IAddressLookupFacade, AddressLookupFacade>();

        return services;
    }
}
