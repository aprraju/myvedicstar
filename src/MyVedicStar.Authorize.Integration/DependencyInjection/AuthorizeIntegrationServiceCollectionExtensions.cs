using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MyVedicStar.ApiProtection.Security;
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

        // SSRF protection (settings: ApiProtection:SsrfProtection). Registered here too so this layer does not depend on
        // the host having called AddMyVedicStarApiProtection.
        services.TryAddTransient<SsrfProtectionHandler>();

        var graphClient = services
            .AddHttpClient<IExternalIdentityService, MicrosoftGraphExternalIdentityService>((serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<MicrosoftGraphOptions>>().Value;
                httpClient.BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl));
            })
            // Redirects are followed by the SSRF handler instead, so every hop is validated.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        // Handlers run in registration order: resilience (retry, timeouts, circuit breaker) wraps the SSRF check,
        // so every attempt is validated.
        graphClient.AddStandardResilienceHandler(configuration.GetSection(MicrosoftGraphResilienceSectionName));
        graphClient.AddHttpMessageHandler<SsrfProtectionHandler>();

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
