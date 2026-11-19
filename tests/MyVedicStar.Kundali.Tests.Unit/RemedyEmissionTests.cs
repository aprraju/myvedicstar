using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// api#128. The production emission rule: nothing not-evaluated is ever persisted.
/// </summary>
/// <remarks>
/// Kept in its own class, apart from the resolver tests, because the two rules apply at different times and the design
/// depends on only one of them acting at read time. A suite that tested them together would not notice if this one
/// started firing on reads too.
/// </remarks>
public sealed class RemedyEmissionTests
{
    private static KundaliRemedy Remedy(string code) =>
        new(code, "evaluated", "behaviour", "Wear yellow on Thursdays.", "Lahiri", "Some published source.");

    [Fact]
    public void Only_evaluated_findings_are_emitted()
    {
        var (emitted, unsupported) = RemedyEmission.Select(
        [
            (Remedy("yoga:gaja-kesari"), RemedyFindingState.Evaluated),
            (Remedy("dosha:mangal"), RemedyFindingState.Withheld),
            (Remedy("yoga:raja"), RemedyFindingState.NotEvaluated),
        ]);

        // Two of three suppressed. If this ever emits all three, a withheld remedy gets persisted and every later read
        // inherits it.
        Assert.Equal("yoga:gaja-kesari", Assert.Single(emitted).FindingCode);
        Assert.Equal(2, unsupported.Count);
    }

    [Fact]
    public void A_suppressed_remedy_is_explained_rather_than_silently_dropped()
    {
        var (_, unsupported) = RemedyEmission.Select(
        [
            (Remedy("dosha:mangal"), RemedyFindingState.Withheld),
            (Remedy("yoga:raja"), RemedyFindingState.NotEvaluated),
        ]);

        // Silent omission is the pattern this codebase refuses: the consumer must be able to tell that a finding had a
        // remedy which was withheld, not that no remedy exists.
        Assert.Contains(unsupported, field =>
            field.Path == "dosha:mangal" && field.Reason == RemedyResolver.Reasons.CapabilityGap);
        Assert.Contains(unsupported, field =>
            field.Path == "yoga:raja" && field.Reason == RemedyResolver.Reasons.InputMissing);
    }

    [Fact]
    public void An_emitted_remedy_carries_the_status_it_was_emitted_under()
    {
        // The stored status lets the UI assert "a remedy is only ever shown beside an evaluated finding" against a
        // stored response. If this were left as whatever the catalogue supplied, the assertion would test the catalogue.
        var (emitted, _) = RemedyEmission.Select([(Remedy("yoga:gaja-kesari"), RemedyFindingState.Evaluated)]);

        Assert.Equal("evaluated", Assert.Single(emitted).FindingStatus);
    }

    [Fact]
    public void An_empty_candidate_set_emits_nothing_and_explains_nothing()
    {
        var (emitted, unsupported) = RemedyEmission.Select([]);

        Assert.Empty(emitted);
        Assert.Empty(unsupported);
    }

    [Fact]
    public void An_unhandled_finding_state_is_refused_rather_than_emitted()
    {
        // Same default-deny discipline as the resolver: a new state must decide for itself whether it may be stored.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RemedyEmission.Select([(Remedy("yoga:gaja-kesari"), (RemedyFindingState)999)]));
    }

    // ---- merging two producers into one unsupported array ----

    [Fact]
    public void Neither_producer_loses_its_entries_in_the_merge()
    {
        // The capability path and the emission rule both write this array. A merge that let one assign over the other
        // would drop entries while both callers believed they had written theirs.
        var fromCapability = new[]
        {
            new KundaliUnsupportedField("Sun", "unvalidated-reference"),
        };
        var fromEmission = new[]
        {
            new KundaliUnsupportedField("dosha:mangal", RemedyResolver.Reasons.CapabilityGap),
        };

        var merged = RemedyEmission.MergeUnsupported(fromCapability, fromEmission);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, field => field.Path == "Sun");
        Assert.Contains(merged, field => field.Path == "dosha:mangal");
    }

    [Fact]
    public void The_same_path_may_carry_two_reasons_and_both_survive()
    {
        // A graha can be withheld for want of a reference AND have a remedy suppressed for the same graha. Collapsing by
        // path alone would lose one of the two explanations.
        //
        // This asserts the STATED guarantee and deliberately no more: two different reasons on one path both survive.
        // It does NOT assert total preservation, because an exact duplicate is collapsed on purpose (see
        // An_exact_duplicate_is_collapsed_once). A test claiming everything survives would fail the day a third producer
        // legitimately reuses a (path, reason) tuple, and the repair would then be to weaken this test rather than to
        // add a distinct reason code - which is the wrong repair.
        var first = new[] { new KundaliUnsupportedField("Moon", "unvalidated-reference") };
        var second = new[] { new KundaliUnsupportedField("Moon", RemedyResolver.Reasons.InputMissing) };

        var merged = RemedyEmission.MergeUnsupported(first, second);

        Assert.Equal(2, merged.Count);
        Assert.Equal(2, merged.Select(field => field.Reason).Distinct().Count());
    }

    [Fact]
    public void An_exact_duplicate_is_collapsed_once()
    {
        var entry = new KundaliUnsupportedField("Moon", "unvalidated-reference");

        var merged = RemedyEmission.MergeUnsupported([entry], [entry]);

        // The same statement twice is noise, not information.
        Assert.Single(merged);
    }

    [Fact]
    public void Order_is_preserved_so_the_most_specific_explanation_stays_first()
    {
        var first = new[] { new KundaliUnsupportedField("Moon", "unvalidated-reference") };
        var second = new[] { new KundaliUnsupportedField("Moon", RemedyResolver.Reasons.InputMissing) };

        var merged = RemedyEmission.MergeUnsupported(first, second);

        Assert.Equal("unvalidated-reference", merged[0].Reason);
    }

    [Fact]
    public void Merging_nothing_yields_nothing()
    {
        Assert.Empty(RemedyEmission.MergeUnsupported());
        Assert.Empty(RemedyEmission.MergeUnsupported([], []));
    }

}

