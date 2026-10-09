namespace MyVedicStar.Authorize.Integration.Identity.MicrosoftGraph;

/// <summary>How the application authenticates to Microsoft Entra when requesting Microsoft Graph tokens.</summary>
public enum MicrosoftGraphCredentialType
{
    /// <summary>App registration client secret, supplied by configuration (Key Vault in Azure).</summary>
    ClientSecret,

    /// <summary>
    /// Managed identity token presented as a federated client assertion, so no secret is stored.
    /// The app registration must have a federated credential that trusts the managed identity.
    /// </summary>
    ManagedIdentityFederation,
}
