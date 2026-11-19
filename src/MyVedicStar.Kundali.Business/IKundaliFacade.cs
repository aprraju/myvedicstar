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


    /// <summary>KUN-07: one divisional chart for an owned profile. A division whose rule is not implemented is rejected
    /// here; a division that is implemented but whose placements need unvalidated inputs is returned with a precision
    /// status that says so.</summary>
    Task<KundaliChartsSection> GetChartsAsync(ClaimsPrincipal user, Guid profileId, int divisional,
        CancellationToken token);


    /// <summary>KUN-08: planet placements plus the dignity/aspect rule capabilities. A placement is only returned when
    /// the engine produced a position for it; the rule list is independent of any chart.</summary>
    Task<KundaliPlanetsSection> GetPlanetsAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);

    /// <summary>
    /// KUN-13: the transit section for an owned profile at an explicit reference instant.
    /// </summary>
    /// <param name="at">The instant to describe. Null means the server clock, and the resolved instant is echoed in the
    /// response so a caller never has to assume which one was used.</param>
    /// <param name="timezone">IANA zone to echo for display. Null means the profile's own zone.</param>
    /// <remarks>An unresolvable timezone is rejected here rather than in the controller, so the rule has one home.</remarks>
    Task<KundaliTransitsSection> GetTransitsAsync(ClaimsPrincipal user, Guid profileId, DateTimeOffset? at, string? timezone,
        CancellationToken token);

    /// <summary>KUN-13: the reader's alert consents for an owned profile. Empty when they have opted out of everything.</summary>
    Task<IReadOnlyList<KundaliAlertPreference>> GetAlertsAsync(ClaimsPrincipal user, Guid profileId,
        CancellationToken token);

    /// <summary>KUN-13: grants consent for the supplied event types over the supplied channel, idempotently.</summary>
    Task<IReadOnlyList<KundaliAlertPreference>> GrantAlertsAsync(ClaimsPrincipal user, Guid profileId,
        IReadOnlyList<string> eventTypes, string channel, CancellationToken token);

    /// <summary>KUN-13: revokes every consent for this profile. Returns the number removed.</summary>
    Task<int> RevokeAlertsAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);

    /// <summary>KUN-23: removes this account's Kundali data for a profile. Called as part of profile deletion so a
    /// deleted profile leaves no Kundali rows behind and every Kundali endpoint then returns 404.</summary>
    /// <returns>The number of aggregates removed; zero means the profile had no Kundali data.</returns>
    Task<int> DeleteForProfileAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);
    /// <summary>KUN-22: applies one profile edit to this module. A birth-input or settings change advances the profile
    /// revision, bumps the aggregate and abandons any in-flight revision; a display-only change (name) touches nothing
    /// calculation-related, so the engine is never called for it. Idempotent on (profile, new revision).</summary>
    Task<KundaliProfileChangeResult> ApplyProfileChangeAsync(ClaimsPrincipal user, Guid profileId,
        long profileRevision, string changeKind, CancellationToken token);

    /// <summary>KUN-22: the background-result admission check. Returns the stored revision summary when the result is
    /// still current, and discards it (returning the reason) when it was computed for an earlier revision or under a
    /// different version tuple. A discarded result is never persisted as current.</summary>
    Task<KundaliBackgroundResultDisposition> DiscardObsoleteResultAsync(ClaimsPrincipal user, Guid profileId,
        Guid revisionId, CancellationToken token);


    /// <summary>KUN-11: bhinnashtakavarga per planet and the sarvashtakavarga aggregate for an owned profile.</summary>
    Task<KundaliAshtakavargaSection> GetAshtakavargaAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);

    /// <summary>KUN-12: Shadbala per planet and Bhava Bala per house, as two separate objects, for an owned profile.</summary>
    Task<KundaliBalaSection> GetBalaAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);

    /// <summary>KUN-10: yoga and dosha assessments. Evaluated and never-evaluated combinations are delivered together,
    /// each with its own completeness and reason.</summary>
    Task<KundaliYogasSection> GetYogasAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);

    /// <summary>KUN-16 remedies (api#128). Only evaluated findings receive a remedy.</summary>
    Task<KundaliRemediesSection> GetRemediesAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token);

    KundaliCapabilityReport GetCapabilities();

    /// <summary>KUN-17: request a report for the named sections. Returns the created report in `queued` state; the
    /// caller polls <see cref="GetReportAsync"/>. A report binds the source revision at request time.</summary>
    Task<KundaliReport> RequestReportAsync(
        ClaimsPrincipal user, Guid profileId, KundaliReportRequest request, CancellationToken token);

    /// <summary>KUN-17: report state and, once ready, an owner-scoped download URL whose expiry is the effective
    /// minimum of the issue window and the report's lifetime.</summary>
    Task<KundaliReport> GetReportAsync(
        ClaimsPrincipal user, Guid profileId, Guid reportId, CancellationToken token);
}

