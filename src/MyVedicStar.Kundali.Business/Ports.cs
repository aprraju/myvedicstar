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

/// <summary>Persistence boundary for the Kundali module. Every keyed operation filters by accountId AND profileId.
/// Implementations must enforce the one-aggregate-per-profile rule atomically and raise
/// <see cref="KundaliConflictException"/> for the loser of a concurrent first generation.</summary>
public interface IKundaliRepository
{
    Task<KundaliView?> GetAsync(Guid accountId, Guid profileId, CancellationToken token);

    /// <summary>Creates the aggregate and its first revision atomically, or returns the existing aggregate when one
    /// already exists for this owner+profile. Idempotent per owner+profile+input hash+config version.</summary>
    Task<KundaliView> EnsureAggregateWithRevisionAsync(Guid accountId, Guid profileId,
        KundaliInputSnapshot snapshot, KundaliProvenance provenance, CancellationToken token);

    /// <summary>Adds a revision to an existing aggregate with an owner-scoped compare-and-swap on the aggregate
    /// revision. A stale revision is a safe conflict, never an overwrite.</summary>
    Task<KundaliView> AddRevisionAsync(Guid accountId, Guid profileId, long aggregateRevision,
        KundaliInputSnapshot snapshot, KundaliProvenance provenance, CancellationToken token);

    /// <summary>Persists one section payload onto a revision and advances the status. A terminal status is never
    /// downgraded by a later intermediate write.</summary>
    Task<KundaliRevisionSummary> SaveSectionAsync(Guid accountId, Guid profileId, Guid revisionId, string section,
        string payloadJson, bool isFinal, string? failureReasonCode, CancellationToken token);

    Task<KundaliPreferences?> GetPreferencesAsync(Guid accountId, Guid profileId, CancellationToken token);

    /// <summary>Upserts display preferences. Presentation-only: never touches revisions or provenance.</summary>
    Task<KundaliPreferences> SavePreferencesAsync(Guid accountId, Guid profileId, string chartStyle,
        long revision, CancellationToken token);
}
