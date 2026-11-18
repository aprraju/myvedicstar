using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>
/// Outcome of admitting a background result for publication (KUN-22). The distinction is the whole point: a result is
/// either still valid and may be shown, or it is obsolete and must be discarded with a reason the caller can log. There
/// is deliberately no "partially usable" state.
/// </summary>
/// <param name="Accepted">True only when the result describes the profile's present revision and version tuple.</param>
/// <param name="ReasonCode">Why the result was discarded; null when accepted. Stable codes, never prose.</param>
/// <param name="Revision">The revision the result belongs to, so a caller can compare it with its own view.</param>
public sealed record KundaliBackgroundResultDisposition(
    bool Accepted,
    string? ReasonCode,
    Guid RevisionId,
    long Revision,
    long ResultProfileRevision,
    long CurrentProfileRevision)
{
    public static class Reasons
    {
        /// <summary>The profile was edited after this result was computed. The superseding revision owns the answer.</summary>
        public const string ProfileRevisionChanged = "profile-revision-changed";

        /// <summary>The engine, ephemeris, rule set, timezone data or ayanamsa changed since this result was computed.
        /// </summary>
        public const string ProvenanceChanged = "provenance-changed";

        /// <summary>The revision is no longer the aggregate's active revision.</summary>
        public const string Superseded = "superseded";

        /// <summary>The revision does not exist for this owner and profile.</summary>
        public const string NotFound = "not-found";
    }
}
