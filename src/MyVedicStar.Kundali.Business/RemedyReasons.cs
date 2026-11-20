namespace MyVedicStar.Kundali.Business;

/// <summary>
/// Why a remedy could not be attached, as a closed set.
/// </summary>
/// <remarks>
/// <para><b>Why an enum rather than string constants.</b> A cause used to be a <c>const string</c>, so a new cause
/// whose text happened to match an existing one was indistinguishable from it - and the merge in
/// <see cref="RemedyEmission.MergeUnsupported"/> keys on (path, reason), so the duplicate vanished while both callers
/// believed they had written their entry. Adding a cause is now adding a member of a closed set.</para>
/// <para><b>Values are stable and must not be renumbered.</b> They may appear in logs. The wire text is what clients
/// see and what the contract promises, and it lives in exactly one place: <see cref="Reasons.ToWire"/>.</para>
/// </remarks>
internal enum RemedyReason
{
    /// <summary>The code is not produced by the current engine result at all.</summary>
    CatalogueStale = 0,

    /// <summary>The code appeared in the stored payload but its status could not be read back.</summary>
    /// <remarks>
    /// <para><b>This is a normal path, not a corruption case.</b> Sections persist per revision by design, so any
    /// remedy that outlives a vocabulary change reaches this state. It is expected traffic that must be handled, not
    /// defensive coding against data that cannot occur - a branch classified the latter way gets deleted in a
    /// tidy-up.</para>
    /// <para>Distinct from <see cref="CatalogueStale"/> on purpose. Both signal a non-attached remedy because neither
    /// may render, but they have different causes and different fixes: one means the catalogue moved on, the other
    /// means the stored payload was written under a vocabulary this build no longer reads. Collapsing them would send
    /// a maintainer to the catalogue for a problem that is in the stored revision.</para>
    /// </remarks>
    StoredStatusUnreadable = 1,

    /// <summary>The finding could not be assessed because an input was missing.</summary>
    InputMissing = 2,

    /// <summary>The finding was assessed but is not published (capability gap).</summary>
    CapabilityGap = 3,
}

/// <summary>Wire text for <see cref="RemedyReason"/>: the only place a reason string is written down.</summary>
/// <remarks>
/// The mapping is exhaustive over the enum, so a new member without wire text is a caught omission rather than a value
/// that silently inherits someone else's string. That property is what lets the merge key on (path, reason) be safe:
/// two distinct causes cannot produce the same key by accident, because distinct causes are distinct members and the
/// map assigns each its own text.
/// </remarks>
public static class Reasons
{
    // ToWire is public because the emission path calls it; the ENUM is internal so a serialiser cannot meet it.
    // See RemedyResolution.ReasonWire for why that matters.

    /// <summary>The stable wire text for a reason.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value cast past the declared members.</exception>
    /// <remarks>
    /// Internal, because its parameter type is internal - and that is the point: the enum cannot flow out to a
    /// serializer, so the wire text is the only form that can leave the assembly.
    /// </remarks>
    internal static string ToWire(RemedyReason reason) => reason switch
    {
        RemedyReason.CatalogueStale => "catalogue-stale",
        RemedyReason.StoredStatusUnreadable => "stored-status-unreadable",
        RemedyReason.InputMissing => "input-missing",
        RemedyReason.CapabilityGap => "capability-gap",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason,
            "Unrecognised remedy reason. Every cause needs its own stable wire text."),
    };

    /// <summary>
    /// The refusal raised when state carries a value outside the declared members.
    /// </summary>
    /// <remarks>
    /// A shared identifier, not shared prose: the producer and the test both name this constant, so rewording the
    /// message cannot leave a test asserting a string nothing emits any more. A message assertion with no shared
    /// identifier is a spell-check rather than a contract.
    /// </remarks>
    public const string UnrecognisedStateMessage =
        "Unrecognised finding state. The resolver is default-deny: an unknown state must never attach a remedy.";
}

