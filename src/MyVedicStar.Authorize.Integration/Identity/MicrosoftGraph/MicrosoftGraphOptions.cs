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

    /// <summary>Client secret of that app registration. Supplied by Key Vault in Azure and user secrets locally; never source control.</summary>
    [Required]
    public string ClientSecret { get; set; } = string.Empty;
}
