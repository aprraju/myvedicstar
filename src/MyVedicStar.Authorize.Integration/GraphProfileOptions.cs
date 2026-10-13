using System.ComponentModel.DataAnnotations;
namespace MyVedicStar.Authorize.Integration;
/// <summary>Backend-only Graph settings. Disabled until tenant consent and credentials are configured.</summary>
public sealed class GraphProfileOptions : IValidatableObject
{
    public const string SectionName = "Integrations:MicrosoftGraph";
    public bool Enabled { get; set; }
    public string TenantId { get; set; } = "";
    public string CredentialType { get; set; } = "ManagedIdentity";
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    [Range(1, 60)] public int TimeoutSeconds { get; set; } = 15;
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enabled) yield break;
        if (!Guid.TryParse(TenantId, out _)) yield return new("Graph TenantId must be a UUID.", [nameof(TenantId)]);
        if (CredentialType is not ("ManagedIdentity" or "ClientSecret")) yield return new("Select a supported Graph credential type.", [nameof(CredentialType)]);
        if (CredentialType == "ClientSecret" && (!Guid.TryParse(ClientId, out _) || string.IsNullOrWhiteSpace(ClientSecret))) yield return new("Graph client-secret authentication needs a client ID and secret.");
    }
}
