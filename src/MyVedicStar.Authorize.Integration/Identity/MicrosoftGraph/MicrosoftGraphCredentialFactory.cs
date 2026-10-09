using Azure.Core;
using Azure.Identity;

namespace MyVedicStar.Authorize.Integration.Identity.MicrosoftGraph;

/// <summary>Creates the credential used to obtain Microsoft Graph app tokens for the configured credential type.</summary>
internal static class MicrosoftGraphCredentialFactory
{
    public static TokenCredential Create(MicrosoftGraphOptions options)
    {
        var authorityHost = new Uri(options.AuthorityHost);

        return options.CredentialType switch
        {
            MicrosoftGraphCredentialType.ClientSecret => new ClientSecretCredential(
                options.TenantId,
                options.ClientId,
                options.ClientSecret,
                new ClientSecretCredentialOptions { AuthorityHost = authorityHost }),
            MicrosoftGraphCredentialType.ManagedIdentityFederation => CreateManagedIdentityFederatedCredential(options, authorityHost),
            _ => throw new InvalidOperationException($"Unsupported Microsoft Graph credential type '{options.CredentialType}'."),
        };
    }

    private static ClientAssertionCredential CreateManagedIdentityFederatedCredential(MicrosoftGraphOptions options, Uri authorityHost)
    {
        var managedIdentity = new ManagedIdentityCredential(string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId));
        var assertionRequest = new TokenRequestContext([$"{options.FederatedTokenAudience.TrimEnd('/')}/.default"]);

        return new ClientAssertionCredential(
            options.TenantId,
            options.ClientId,
            async cancellationToken => (await managedIdentity.GetTokenAsync(assertionRequest, cancellationToken)).Token,
            new ClientAssertionCredentialOptions { AuthorityHost = authorityHost });
    }
}
