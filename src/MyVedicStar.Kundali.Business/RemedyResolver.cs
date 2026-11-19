using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>
/// How a remedy attaches to the finding set it was written against (api#128, §4a of the design).
/// </summary>
/// <remarks>
/// <para><b>Four members, and the set is closed.</b> A remedy can attach, or it can fail to attach in three
/// distinguishable ways, and those three are not interchangeable: they tell three different people three different
/// things. Completeness here is what lets <see cref="RemedyResolver"/> switch without a <c>default</c>, so a fifth
/// outcome added later is a build failure rather than a silent fall-through to the one branch that renders a remedy
/// to a user.</para>
/// <para>The numbering is explicit and non-contiguous-safe: never renumber, because the value may appear in logs and
/// in a stored section payload.</para>
/// </remarks>
public enum RemedyOutcome
{
    /// <summary>The code resolved to a finding the engine evaluated. The only outcome that renders a remedy.</summary>
    Attached = 0,

    /// <summary>The code resolved to a finding the engine could not assess, because an input was missing.
    /// The finding exists; the engine could not judge it.</summary>
    NotEvaluated = 1,

    /// <summary>The code resolved to a finding that was assessed but is not published (capability gap).
    /// The finding exists and was judged; publication is withheld.</summary>
    Withheld = 2,

    /// <summary>The code resolved to no finding at all in this result - the catalogue entry is stale or superseded.</summary>
    UnknownCode = 3,
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
public sealed record RemedyResolution(RemedyOutcome Outcome, string FindingCode, string? Reason);

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
            return new RemedyResolution(RemedyOutcome.UnknownCode, findingCode,
                "This remedy refers to a finding that is not produced for this chart. "
                + "The catalogue entry is stale or superseded.");

        return state switch
        {
            RemedyFindingState.Evaluated => new RemedyResolution(RemedyOutcome.Attached, findingCode, null),

            RemedyFindingState.NotEvaluated => new RemedyResolution(RemedyOutcome.NotEvaluated, findingCode,
                "This remedy's finding could not be assessed because an input was missing. "
                + "Completing the birth details may make it available."),

            RemedyFindingState.Withheld => new RemedyResolution(RemedyOutcome.Withheld, findingCode,
                "This remedy's finding is not published yet."),

            // Exhaustive for the current enum, and no default arm. Adding a state to RemedyFindingState makes this
            // throw at construction of the switch's result, which is a build failure - not a silent attachment.
            _ => throw new ArgumentOutOfRangeException(nameof(findingCode), state,
                "Unhandled finding state. The resolver is default-deny: an unrecognised state must never attach a remedy."),
        };
    }
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

