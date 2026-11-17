using System.Security.Claims;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>Read-only access to the owned Vedic Profile that supplies birth inputs. Never writes profile data and
/// never exposes a client-supplied owner.</summary>
public interface IKundaliProfileReader
{
    Task<Guid> GetRegisteredAccountIdAsync(ClaimsPrincipal user, CancellationToken token);

    /// <summary>Returns an immutable snapshot for an owned profile, or null when the profile is not owned.
    /// A foreign id and a missing id are indistinguishable here on purpose (the facade maps both to 404).
    /// Reads are performed as the supplied principal so ownership is enforced by the owning module.</summary>
    Task<KundaliInputSnapshot?> GetSnapshotAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);

    /// <summary>Resolves the effective calculation settings (ayanamsa) from the existing application Settings.
    /// There is no Kundali-specific settings store.</summary>
    Task<string> GetAyanamsaAsync(ClaimsPrincipal user, CancellationToken token);
}
