using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Business.Options;

/// <summary>
/// Which validated token claims describe the signed-in user. Each list is checked in order and the first
/// non-blank value wins. All values are supplied by configuration and validated at startup.
/// </summary>
public sealed class UserIdentityClaimOptions
{
    public const string SectionName = "Authorize:UserIdentityClaims";

    /// <summary>
    /// Claim types for the user's directory object ID, for example <c>oid</c>. Must be the object ID, not the
    /// per-application <c>sub</c>, because it is the stable key the user is stored under.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string[] ExternalUserId { get; set; } = [];

    /// <summary>Claim types for the user's display name, for example <c>name</c>. Optional: none means no name.</summary>
    public string[] Name { get; set; } = [];

    /// <summary>Claim types for the user's email address, for example <c>email</c>. Optional: none means no email.</summary>
    public string[] Email { get; set; } = [];
}
