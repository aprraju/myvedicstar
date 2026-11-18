using System.Security.Claims;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Kundali.Business;

/// <summary>
/// The two claims an internally authenticated caller (a background queue consumer, a scheduled worker) must present for
/// the module to act on an owned profile. The API host never trusts caller-supplied identity: this type exists for the
/// internal contract, and the claim names are the host's own.
/// </summary>
public static class KundaliPrincipal
{
    /// <summary>Stable issuer for principals this module constructs for internal calls.</summary>
    public const string InternalIssuer = "myvedicstar.internal.kundali";

    /// <summary>Identifies the account the work is for. Never accepted from a request body or query string.</summary>
    public const string AccountClaim = "myvedicstar:account-id";

    /// <summary>Identifies the calling process, for audit only. Grants no ownership by itself.</summary>
    public const string CallerClaim = "myvedicstar:internal-caller";

    /// <summary>Builds the principal a worker uses to act on its own account's profile. There is no default account:
    /// an absent or malformed id throws rather than silently acting on someone else's data.</summary>
    public static ClaimsPrincipal ForAccount(Guid accountId, string caller)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);
        if (accountId == Guid.Empty)
            throw new ValidationException("An internal Kundali call requires a concrete account id.");

        var identity = new ClaimsIdentity([
            new Claim(AccountClaim, accountId.ToString("D")),
            new Claim(CallerClaim, caller)
        ], InternalIssuer);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>True only for a principal this type built: the issuer must match and the account claim must parse.</summary>
    public static bool IsInternal(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && user.Identity.AuthenticationType == InternalIssuer
        && Guid.TryParse(user.FindFirst(AccountClaim)?.Value, out var id) && id != Guid.Empty;
}
