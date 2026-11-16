using System.Security.Claims;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.VedicProfiles.Business;

/// <summary>Read-only account integration. Never creates or updates account/Entra identity.</summary>
public interface IVedicProfileAccountReader
{
    Task<Guid> GetRegisteredAccountIdAsync(ClaimsPrincipal user, CancellationToken token);
    /// <summary>Read First + Last name only for a blank Self name on creation; null if unavailable.</summary>
    Task<string?> GetDefaultNameAsync(ClaimsPrincipal user, CancellationToken token);
}

/// <summary>Persistence boundary. Every keyed query/mutation MUST filter by accountId AND id.
/// Create/replace MUST enforce unique Self atomically (including races), raising
/// VedicProfileConflictException(DuplicateSelf). No private caching or account writes.</summary>
public interface IVedicProfileRepository
{
    Task<IReadOnlyList<VedicProfileSummary>> ListAsync(Guid accountId, CancellationToken token);
    Task<VedicProfile?> GetAsync(Guid accountId, Guid id, CancellationToken token);
    /// <summary>Persist a new resource with server-generated id and revision 1.</summary>
    Task<VedicProfile> CreateAsync(Guid accountId, VedicProfileDetails details, CancellationToken token);
    /// <summary>Atomic owner-scoped compare-and-swap; return incremented revision.
    /// Missing/not-owned => ResourceNotFoundException, stale => VedicProfileConflictException(StaleRevision).
    /// Never overwrite on stale revision or overflow.</summary>
    Task<VedicProfile> ReplaceAsync(Guid accountId, Guid id, VedicProfileDetails details, long revision, CancellationToken token);
    /// <summary>Atomic owner-scoped delete; false means missing/not-owned. No revision required.</summary>
    Task<bool> DeleteAsync(Guid accountId, Guid id, CancellationToken token);
}
