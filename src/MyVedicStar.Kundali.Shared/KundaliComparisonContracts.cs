namespace MyVedicStar.Kundali.Shared;

/// <summary>
/// KUN-15: one category of an Ashtakoota comparison. A category is either SCORED (has <c>Score</c>) or INDETERMINATE
/// (has <c>Reason</c> and <c>Side</c>), never both and never neither.
/// </summary>
/// <remarks>
/// <para><b>The discriminator is the point.</b> The UI must be able to tell "evaluated and scored 0" from "never
/// evaluated". A response carrying only a number cannot express that distinction, and a consumer receiving <c>0</c>
/// where nothing was computed renders a false negative. This shape therefore has two representations rather than one
/// nullable number, and the schema is documented in <see cref="KundaliAshtakootaResponse"/>.</para>
/// <para><c>Maximum</c> is always present, even for an indeterminate category: the UI shows "unavailable" against a
/// known denominator rather than inferring one.</para>
/// </remarks>
/// <param name="Code">Koota code: Varna, Vashya, Tara, Yoni, GrahaMaitri, Gana, Bhakoot, Nadi.</param>
/// <param name="Name">Display name.</param>
/// <param name="Maximum">Points this koota can award.</param>
/// <param name="Score">Awarded points, or null when the category is indeterminate.</param>
/// <param name="Reason">Machine-readable reason, present exactly when <paramref name="Score"/> is null.</param>
/// <param name="Side">Which side made the category indeterminate ("A" or "B"), when one did.</param>
/// <param name="Note">Short explanation of the score, or of why it is absent.</param>
public sealed record KundaliAshtakootaCategory(
    string Code,
    string Name,
    int Maximum,
    int? Score,
    string? Reason,
    string? Side,
    string Note)
{
    /// <summary>True when the category was evaluated. The UI branches on this, not on the number.</summary>
    public bool IsScored => Score.HasValue;
}

/// <summary>
/// One side of a comparison: the profile identity the caller may see and the revision its Moon was read at.
/// </summary>
/// <remarks>
/// No birth data, no coordinates, no name. The comparison speaks in ids and revisions only, so the response cannot leak
/// one profile's private inputs into another account's request even if ownership checks were to regress elsewhere.
/// </remarks>
public sealed record KundaliComparisonSide(Guid ProfileId, long Revision);

/// <summary>
/// The Manglik comparison for two profiles.
/// </summary>
/// <remarks>
/// Reported per side rather than as a combined verdict: whether two Mangliks are "compatible" is an interpretation,
/// and the KUN-15 decision is scores without verdict. <c>Status</c> per side says what was determined.
/// </remarks>
public sealed record KundaliManglikComparison(
    string StatusA,
    string StatusB,
    string Note);

/// <summary>
/// KUN-15: the Ashtakoota comparison of two owned profiles.
/// </summary>
/// <remarks>
/// <para><b>There is no verdict field, deliberately.</b> The owner has left the minimum acceptable-match threshold
/// OPEN. This response reports a score, the maximum it is out of, and a certainty; it does not say "good match" or
/// "poor match", and no field exists that a UI could mistake for one. If a threshold ships later it arrives as API data
/// with its own provenance, never as a client constant.</para>
/// <para><b>Cache identity is (pairKey, revisionA, revisionB, rulesVersion)</b> and carries no wall-clock TTL. Freshness
/// is a property of the identity: a stored comparison is correct while both revisions and the rule version are
/// unchanged, and wrong the moment any of them moves - never merely "old".</para>
/// </remarks>
/// <param name="PairKey">Order-independent identity: both profile ids sorted ordinally and joined. A-to-B and B-to-A
/// are one comparison and therefore one cache entry.</param>
/// <param name="ProfileA">The first side, in <paramref name="PairKey"/> order.</param>
/// <param name="ProfileB">The second side, in <paramref name="PairKey"/> order.</param>
/// <param name="Categories">All eight categories, in classical order. Unscored ones carry a reason.</param>
/// <param name="Total">Points earned across scored categories.</param>
/// <param name="ScoredMaximum">Points available across the scored categories only. Never null; 0 when nothing scored.</param>
/// <param name="Maximum">Points available overall, 36.</param>
/// <param name="Certainty">complete or partial.</param>
/// <param name="Manglik">The Manglik comparison.</param>
/// <param name="RulesVersion">The rule table version the scores were computed under.</param>
public sealed record KundaliAshtakootaComparison(
    string PairKey,
    KundaliComparisonSide ProfileA,
    KundaliComparisonSide ProfileB,
    IReadOnlyList<KundaliAshtakootaCategory> Categories,
    int Total,
    int ScoredMaximum,
    int Maximum,
    string Certainty,
    KundaliManglikComparison Manglik,
    string RulesVersion);

/// <summary>Error codes specific to comparisons, beyond the module-wide <see cref="KundaliErrorCodes"/>.</summary>
public static class KundaliComparisonErrorCodes
{
    /// <summary>Both path segments name the same profile. A profile is not comparable to itself.</summary>
    public const string SameProfile = "same-profile";

    /// <summary>Either profile's stored comparison state is behind its current revision (409).</summary>
    public const string StaleRevision = "stale-revision";
}
