using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>
/// How a remedy attaches to the finding set it was written against (api#128, §4a of the design).
/// </summary>
/// <remarks>
/// <para><b>Four members, and the set is closed.</b> A remedy can attach, or it can fail to attach in three
/// distinguishable ways, and those three are not interchangeable: they tell three different people three different
/// things. The switch in <see cref="RemedyResolver"/> names all four and adds an explicit refusing arm, so an
/// unrecognised value cannot fall through to the one branch that renders a remedy to a user. Note that this is a
/// runtime refusal, not a compile-time one: it was measured that adding a fifth member to the enum does not fail the
/// build in this repository.</para>
/// <para>The numbering is explicit and non-contiguous-safe: never renumber, because the value may appear in logs and
/// in a stored section payload.</para>
/// </remarks>
public enum RemedyOutcome
{
    /// <summary>Reserved. <c>default(RemedyOutcome)</c> yields this and it is deliberately not a usable outcome, so an
    /// uninitialised value cannot read as <see cref="Attached"/> - the only outcome that renders a remedy.</summary>
    Unspecified = 0,

    /// <summary>The code resolved to a finding the engine evaluated. The only outcome that renders a remedy.</summary>
    Attached = 1,

    /// <summary>The code resolved to a finding the engine could not assess, because an input was missing.
    /// The finding exists; the engine could not judge it.</summary>
    NotEvaluated = 2,

    /// <summary>The code resolved to a finding that was assessed but is not published (capability gap).
    /// The finding exists and was judged; publication is withheld.</summary>
    Withheld = 3,

    /// <summary>The code resolved to no finding at all in this result - the catalogue entry is stale or superseded.</summary>
    UnknownCode = 4,
}

/// <summary>
/// The result of resolving one stored remedy against the finding set of the result being served.
/// </summary>
/// <param name="Outcome">Which of the four cases applies.</param>
/// <param name="FindingCode">The code that was resolved. Always present, so a reader can attribute a non-attached
/// outcome to a specific catalogue entry.</param>
/// <param name="Reason">Why this outcome applies. Non-null for every non-attached outcome; deliberately nullable for
/// <see cref="RemedyOutcome.Attached"/> because an attachment needs no explanation - the same shape
/// <c>KundaliChartPrecision.Status</c> uses.</param>
public sealed record RemedyResolution(RemedyOutcome Outcome, string FindingCode)
{
    /// <summary>
    /// The reason as its stable wire text, or null when the outcome needs no explanation.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the only way a reason becomes text, and <see cref="Reason"/> is deliberately not public.</b> If
    /// the enum were public, a serializer meeting it would emit the member name - <c>"CatalogueStale"</c> - while every
    /// test still passed, because no test asserts what the serializer does with the enum. The wire value would then be
    /// decided by reflection rather than by <see cref="Reasons.ToWire"/>, and the closed set would protect nothing at
    /// the boundary it exists for.</para>
    /// <para>Keeping the enum internal means it cannot reach a serializer at all: there is no public surface to
    /// serialize it from. Tests are in a separate assembly and reach the reason through
    /// <c>InternalsVisibleTo</c> if needed.</para>
    /// </remarks>
    /// <summary>
    /// Why this outcome applies. Internal, so it cannot reach a serializer: see <see cref="ReasonWire"/>.
    /// </summary>
    internal RemedyReason? Reason { get; init; }

    /// <summary>The reason as its stable wire text, or null when the outcome needs no explanation.</summary>
    public string? ReasonWire => Reason is { } reason ? Reasons.ToWire(reason) : null;

    /// <summary>Builds a resolution carrying a reason.</summary>
    internal static RemedyResolution WithReason(RemedyOutcome outcome, string findingCode, RemedyReason reason) =>
        new(outcome, findingCode) { Reason = reason };
}

/// <summary>
/// Decides whether a stored remedy may be shown, by resolving its finding code against the current finding set.
/// </summary>
/// <remarks>
/// <para><b>Why a resolver exists at all.</b> A remedy section is produced once and served many times: sections are
/// persisted per revision, so a remedy written when its finding was evaluated can be read long after the engine, the
/// ayanamsa or the capability set changed. The stored payload is therefore <i>not</i> a statement about the current
/// chart - it is a statement about the chart at the time it was produced.</para>
/// <para><b>Why this is the only suppression point at read time.</b> Production refuses to write a remedy beside a
/// finding that was not evaluated, so nothing withheld is ever stored. If that refusal were re-applied here, the
/// <see cref="RemedyOutcome.Withheld"/> and <see cref="RemedyOutcome.NotEvaluated"/> branches could only fire on data
/// that cannot exist - dead code guarding a rule already enforced elsewhere, and the day someone deletes one of the
/// two guards nothing fails. Two plausible suppression points is worse than one, so there is one.</para>
/// <para><b>Default-deny.</b> The switch below has no <c>default</c> arm and no fall-through. <see cref="RemedyOutcome.Attached"/>
/// is returned only when a finding was positively located and positively evaluated; every other path returns one of
/// the three non-attached outcomes. Writing this the other way round - <c>if unknown ... else if withheld ... else
/// Attached</c> - would make an unrecognised state render a remedy and nothing would fail.</para>
/// </remarks>
public static class RemedyResolver
{



    /// <summary>
    /// Resolves one remedy's finding code against the current findings.
    /// </summary>
    /// <param name="findingCode">The code the remedy was written against. Required.</param>
    /// <param name="findings">The current finding set, by code. Absent codes are <see cref="RemedyOutcome.UnknownCode"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="findingCode"/> is null or blank. A remedy with no code cannot
    /// be attributed to anything, so it is refused rather than resolved to an outcome.</exception>
    public static RemedyResolution Resolve(string findingCode, IReadOnlyDictionary<string, RemedyFindingState> findings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(findingCode);
        ArgumentNullException.ThrowIfNull(findings);

        // Default-deny, spelled out: the attaching case is the one that must be positively proved, so it is the case
        // that requires a located finding AND an evaluated state. Everything else is a refusal with its own reason.
        if (!findings.TryGetValue(findingCode, out var state))
            return RemedyResolution.WithReason(RemedyOutcome.UnknownCode, findingCode, RemedyReason.CatalogueStale);

        // No discard arm - and MEASURED, NOT ASSUMED: adding a fifth member to RemedyFindingState did NOT fail the
        // build here. CS8509 did not fire and TreatWarningsAsErrors had nothing to escalate, and the reason is worth
        // recording because it is easy to get backwards: CS8509 applies to a switch that is non-exhaustive AND has no
        // discard arm. Removing "_" does not by itself produce the diagnostic; without it the expression is simply
        // non-exhaustive and the compiler is silent here. So the type does not enforce the closed set.
        //
        // An explicit refusing arm is therefore KEPT, because the compiler will not supply one. That makes
        // An_undefined_finding_state_cannot_reach_an_attached_outcome LOAD-BEARING rather than belt-and-braces: it is
        // the guarantee, not a duplicate of it. Do not delete it as redundant, and do not describe any of this as a
        // build failure.
        return state switch
        {
            RemedyFindingState.Evaluated => new RemedyResolution(RemedyOutcome.Attached, findingCode),
            RemedyFindingState.NotEvaluated => RemedyResolution.WithReason(RemedyOutcome.NotEvaluated,
                findingCode, RemedyReason.InputMissing),
            RemedyFindingState.Withheld => RemedyResolution.WithReason(RemedyOutcome.Withheld,
                findingCode, RemedyReason.CapabilityGap),

            // Reachable only for a value cast past the declared members. It is here because the compiler does not
            // enforce the closed set in this repository (see the note above), so an unrecognised state must refuse
            // explicitly rather than fall through to whatever a future compiler emits for a non-exhaustive switch.
            _ => throw new ArgumentOutOfRangeException(nameof(findingCode), state,
                Reasons.UnrecognisedStateMessage),
        };

    }

    /// <summary>
    /// Resolves a remedy that came from a STORED section payload, where the status text was written under whatever
    /// vocabulary the producing build had.
    /// </summary>
    /// <param name="findingCode">The code the stored remedy was written against.</param>
    /// <param name="storedStatusText">The status text as stored. May be unreadable if this build no longer knows it.</param>
    /// <param name="findings">The current finding set, by code.</param>
    /// <remarks>
    /// <para>This exists because parsing protects the wire-to-enum direction and <see cref="Resolve"/> protects
    /// enum-to-decision, but neither protects against a payload that was <i>stored</i> under a vocabulary this build no
    /// longer reads. Sections persist per revision, so that payload is served long after it was written.</para>
    /// <para>An unreadable stored status resolves to <see cref="RemedyOutcome.UnknownCode"/> - it must not render - but
    /// its reason is <see cref="RemedyReason.StoredStatusUnreadable"/>, not <see cref="RemedyReason.CatalogueStale"/>. Same
    /// outcome, different cause, different fix: the catalogue is fine, the stored revision is the problem.</para>
    /// </remarks>
    public static RemedyResolution ResolveStored(string findingCode, string? storedStatusText,
        IReadOnlyDictionary<string, RemedyFindingState> findings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(findingCode);

        // A readable status delegates to the normal path, which is where the finding set is consulted. Note that a
        // readable "evaluated" does NOT attach on its own: Resolve still requires the code to be present AND
        // evaluated in the CURRENT findings. The stored text is a claim by the producing build, not evidence about
        // this one.
        if (IsKnownStatusText(storedStatusText))
            return Resolve(findingCode, findings);

        return RemedyResolution.WithReason(RemedyOutcome.UnknownCode, findingCode, RemedyReason.StoredStatusUnreadable);
    }

    private static bool IsKnownStatusText(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "evaluated" or "not-evaluated" or "withheld" => true,
        // No default arm: an unrecognised stored status is the case this method exists to name.
        _ => false,
    };

}

/// <summary>The state of one finding in the current result, as far as remedy attachment is concerned.</summary>
public enum RemedyFindingState
{
    /// <summary>The engine assessed this finding.</summary>
    Evaluated = 0,

    /// <summary>The engine could not assess it - an input was missing.</summary>
    NotEvaluated = 1,

    /// <summary>Assessed, but not published: a capability gap.</summary>
    Withheld = 2,
}

