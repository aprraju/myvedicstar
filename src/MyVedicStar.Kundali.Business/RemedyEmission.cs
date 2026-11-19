using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>
/// The production-time rule for remedies: a remedy is never written into a section beside a finding that was not
/// evaluated when the section was produced.
/// </summary>
/// <remarks>
/// <para><b>Where this sits relative to <see cref="RemedyResolver"/>.</b> This is the emission rule and it applies at
/// production ONLY. It guarantees that nothing withheld or unassessed is ever persisted. It is deliberately NOT
/// re-applied at read time: sections persist per revision, so a remedy written while its finding was evaluated can be
/// served after the engine or the capability set changed, and the resolver is what reports that. Re-applying this rule
/// on read would make the resolver's non-attached branches unreachable - dead code guarding a rule already enforced
/// here - and the day someone deleted one of the two guards nothing would fail.</para>
/// <para><b>Why the rule exists at all, given the resolver refuses anyway.</b> A withheld remedy that is never stored
/// cannot leak through any read path, including one added later that forgets to resolve. The stored payload should not
/// contain a claim the producing build could not stand behind.</para>
/// </remarks>
public static class RemedyEmission
{
    /// <summary>
    /// Filters a candidate remedy set down to those whose finding was evaluated at production time.
    /// </summary>
    /// <param name="candidates">Remedies the catalogue offers for this chart.</param>
    /// <param name="findings">Each candidate's finding state as of this production run.</param>
    /// <returns>
    /// The emittable remedies, and one <see cref="KundaliUnsupportedField"/> per suppressed finding so the omission is
    /// attributable rather than silent. A suppressed remedy is not merely dropped: the consumer is told which finding
    /// carried no remedy and why, on the same principle the rest of the section uses.
    /// </returns>
    public static (IReadOnlyList<KundaliRemedy> Emitted, IReadOnlyList<KundaliUnsupportedField> Unsupported) Select(
        IEnumerable<(KundaliRemedy Remedy, RemedyFindingState State)> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var emitted = new List<KundaliRemedy>();
        var unsupported = new List<KundaliUnsupportedField>();

        foreach (var (remedy, state) in candidates)
        {
            switch (state)
            {
                case RemedyFindingState.Evaluated:
                    // The only case that may be persisted: the finding was assessed, so a remedy beside it is a claim
                    // the producing build can stand behind.
                    emitted.Add(remedy with
                    {
                        FindingStatus = KundaliRemedyStatuses.ToText(KundaliRemedyFindingStatus.Evaluated),
                    });
                    break;

                case RemedyFindingState.NotEvaluated:
                    unsupported.Add(new KundaliUnsupportedField(remedy.FindingCode,
                        RemedyResolver.Reasons.InputMissing));
                    break;

                case RemedyFindingState.Withheld:
                    unsupported.Add(new KundaliUnsupportedField(remedy.FindingCode,
                        RemedyResolver.Reasons.CapabilityGap));
                    break;

                // No default arm: a new finding state must decide whether it may be persisted. Falling through would
                // silently emit a remedy, which is the failure this class exists to prevent.
                default:
                    throw new ArgumentOutOfRangeException(nameof(candidates), state,
                        "Unhandled finding state at production. A new state must state whether it may be emitted.");
            }
        }

        return (emitted, unsupported);
    }

    /// <summary>
    /// Merges the unsupported entries from independent producers into one list, without loss and without duplicates.
    /// </summary>
    /// <param name="sources">Each producer's entries, in section order.</param>
    /// <remarks>
    /// <para>Two producers write this array: the capability path (withheld grahas, unvalidated references) and
    /// <see cref="Select"/> (remedies suppressed at production). If either assigned the array rather than appending to
    /// it, the other's entries would vanish while both callers believed they had written theirs - a decision turning
    /// back into silence.</para>
    /// <para>De-duplication is by <see cref="KundaliUnsupportedField.Path"/> plus reason, not by path alone: one path can
    /// legitimately carry two different reasons (a graha withheld for want of a reference, and a remedy suppressed for
    /// the same graha), and collapsing those would lose one of the two explanations. Exact duplicates - same path and
    /// same reason - are collapsed, because the same statement twice is noise rather than information.</para>
    /// </remarks>
    public static IReadOnlyList<KundaliUnsupportedField> MergeUnsupported(
        params IEnumerable<KundaliUnsupportedField>[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var seen = new HashSet<(string Path, string Reason)>();
        var merged = new List<KundaliUnsupportedField>();

        foreach (var source in sources)
        {
            if (source is null) continue;
            foreach (var field in source)
            {
                if (field is null) continue;
                // Keep first occurrence: sources are in section order, so the earliest explanation is the most specific.
                if (seen.Add((field.Path, field.Reason))) merged.Add(field);
            }
        }

        return merged;
    }

}

