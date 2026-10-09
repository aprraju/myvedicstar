using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Integration.Identity;
using MyVedicStar.Authorize.Integration.Identity.MicrosoftGraph;

namespace MyVedicStar.Authorize.Integration.DependencyInjection;

public static class AuthorizeIntegrationServiceCollectionExtensions
{
    private const string MicrosoftGraphResilienceSectionName = MicrosoftGraphOptions.SectionName + ":Resilience";

    /// <summary>
    /// Registers the Authorize domain's external identity provider adapters.
    /// </summary>
    public static IServiceCollection AddAuthorizeIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MicrosoftGraphOptions>()
            .Bind(configuration.GetSection(MicrosoftGraphOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IExternalIdentityService, MicrosoftGraphExternalIdentityService>((serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<MicrosoftGraphOptions>>().Value;
                httpClient.BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl));
            })
            .AddStandardResilienceHandler(configuration.GetSection(MicrosoftGraphResilienceSectionName));

        // One credential instance so app tokens are cached and reused until they near expiry.
        services.AddSingleton<TokenCredential>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MicrosoftGraphOptions>>().Value;
            return new ClientSecretCredential(
                options.TenantId,
                options.ClientId,
                options.ClientSecret,
                new ClientSecretCredentialOptions { AuthorityHost = new Uri(options.AuthorityHost) });
        });

        return services;
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
