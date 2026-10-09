using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Business.Options;

/// <summary>
/// Which validated token claims identify the user in the identity provider's directory.
/// All values are supplied by configuration and validated at startup.
/// </summary>
public sealed class UserIdentityClaimOptions
{
    public const string SectionName = "Authorize:UserIdentityClaims";

    /// <summary>
    /// Claim types checked in order for the user's directory object ID, for example <c>oid</c>.
    /// Must be the object ID (not the per-application <c>sub</c>) because it is used to look the user up in Microsoft Graph.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string[] ExternalUserId { get; set; } = [];
}
