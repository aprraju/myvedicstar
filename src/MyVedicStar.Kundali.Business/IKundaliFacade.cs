using System.Security.Claims;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>
/// Sole public business entry point for the Kundali module. The owner is always the authenticated principal; no
/// caller supplies an owner id. Cross-account and unknown profile ids both surface as a 404, never a
/// 403-with-detail, so a caller cannot probe for the existence of another account's profile.
/// </summary>
public interface IKundaliFacade
{
    /// <summary>Reads the aggregate and current revision for an owned profile (KUN-03).</summary>
    Task<KundaliView> GetAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);

    /// <summary>Starts a generation. Idempotent per profile + input hash + config version, so repeated clicks enqueue
    /// once (KUN-04). Returns the current view with the revision that is now running.</summary>
    Task<KundaliView> GenerateAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);

    /// <summary>Retries a failed generation for the same snapshot and settings.</summary>
    Task<KundaliView> RetryAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);

    Task<KundaliPreferences> GetPreferencesAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);
    /// <summary>KUN-05 overview slice: birth details, conventions and the astrological blocks that can be produced.</summary>
    /// <remarks>Blocks that cannot be computed are returned null with an entry in <c>Unsupported</c> carrying a
    /// reason code, so a missing calculation engine is never presented to the user as birth-time uncertainty.</remarks>
    Task<KundaliOverview> GetOverviewAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);


    Task<KundaliPreferences> SavePreferencesAsync(ClaimsPrincipal user, Guid profileId, string chartStyle,
        long revision, CancellationToken token);

    /// <summary>The published capability boundary (ADR-0016 section 3). Account-independent; exposed through the
    /// facade so the API has a single business entry point.</summary>

    /// <summary>KUN-09: the dasha timeline for an owned profile, with the mahadasha chain and the period path at the
    /// query instant. An unsupported system is rejected here rather than in the controller, so the rule has one home.
    /// </summary>
    Task<KundaliDashaTimeline> GetDashasAsync(ClaimsPrincipal user, Guid profileId, string system,
        DateTimeOffset? at, CancellationToken token);

    KundaliCapabilityReport GetCapabilities();
}
