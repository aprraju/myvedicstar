using Azure.Core;
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
            .Validate(
                options => options.CredentialType != MicrosoftGraphCredentialType.ClientSecret || !string.IsNullOrWhiteSpace(options.ClientSecret),
                $"{MicrosoftGraphOptions.SectionName}:{nameof(MicrosoftGraphOptions.ClientSecret)} is required when {nameof(MicrosoftGraphOptions.CredentialType)} is {nameof(MicrosoftGraphCredentialType.ClientSecret)}.")
            .Validate(
                options => options.CredentialType != MicrosoftGraphCredentialType.ManagedIdentityFederation || Uri.IsWellFormedUriString(options.FederatedTokenAudience, UriKind.Absolute),
                $"{MicrosoftGraphOptions.SectionName}:{nameof(MicrosoftGraphOptions.FederatedTokenAudience)} must be an absolute URI when {nameof(MicrosoftGraphOptions.CredentialType)} is {nameof(MicrosoftGraphCredentialType.ManagedIdentityFederation)}.")
            .ValidateOnStart();

        services.AddHttpClient<IExternalIdentityService, MicrosoftGraphExternalIdentityService>((serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<MicrosoftGraphOptions>>().Value;
                httpClient.BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl));
            })
            .AddStandardResilienceHandler(configuration.GetSection(MicrosoftGraphResilienceSectionName));

        // One credential instance so app tokens are cached and reused until they near expiry.
        services.AddSingleton<TokenCredential>(serviceProvider =>
            MicrosoftGraphCredentialFactory.Create(serviceProvider.GetRequiredService<IOptions<MicrosoftGraphOptions>>().Value));

        return services;
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
