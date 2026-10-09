using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Integration.Identity.MicrosoftGraph;

/// <summary>
/// Settings for calling Microsoft Graph as this application (client credentials).
/// All values are supplied by configuration and validated at startup.
/// </summary>
public sealed class MicrosoftGraphOptions
{
    public const string SectionName = "Integrations:MicrosoftGraph";

    /// <summary>Graph API base URL including version, for example <c>https://graph.microsoft.com/v1.0/</c>.</summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Token scopes requested for Graph, for example <c>https://graph.microsoft.com/.default</c>.</summary>
    [Required]
    [MinLength(1)]
    public string[] Scopes { get; set; } = [];

    /// <summary>Microsoft Entra authority host used to obtain app tokens, for example <c>https://login.microsoftonline.com/</c>.</summary>
    [Required]
    [Url]
    public string AuthorityHost { get; set; } = string.Empty;

    /// <summary>Directory (tenant) ID of the Entra External ID tenant.</summary>
    [Required]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Application (client) ID of the app registration granted Graph <c>User.Read.All</c> application permission.</summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>How this application proves its identity when requesting Graph tokens.</summary>
    public MicrosoftGraphCredentialType CredentialType { get; set; } = MicrosoftGraphCredentialType.ClientSecret;

    /// <summary>
    /// Client secret of that app registration. Required when <see cref="CredentialType"/> is
    /// <see cref="MicrosoftGraphCredentialType.ClientSecret"/>. Keep it in user secrets or a vault, never in source control.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Client ID of the user-assigned managed identity used with <see cref="MicrosoftGraphCredentialType.ManagedIdentityFederation"/>.
    /// Leave empty to use the system-assigned identity.
    /// </summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;

    /// <summary>
    /// Audience of the managed identity token presented as a federated client assertion, for example
    /// <c>api://AzureADTokenExchange</c> in the public cloud.
    /// </summary>
    public string FederatedTokenAudience { get; set; } = string.Empty;
}
