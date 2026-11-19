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
}

