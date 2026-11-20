using MyVedicStar.Kundali.Business;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// api#128. The resolver's four outcomes, tested before the happy path exists to lean on.
/// </summary>
/// <remarks>
/// The ordering is deliberate and is the point of the file: the three refusal branches are asserted first, because
/// <c>Attached</c> is the only outcome that renders a remedy to a user and it is the one that must never be reached by
/// fall-through. A test suite that establishes the happy path first makes the refusal branches look like additions.
/// </remarks>
public sealed class RemedyResolverTests
{
    private static Dictionary<string, RemedyFindingState> Findings(params (string Code, RemedyFindingState State)[] entries) =>
        entries.ToDictionary(entry => entry.Code, entry => entry.State, StringComparer.Ordinal);

    // ---- the three refusals, first ----

    [Fact]
    public void A_code_that_resolves_to_no_finding_is_unknown_not_attached()
    {
        var resolution = RemedyResolver.Resolve("dosha:mangal", Findings(("yoga:gaja-kesari", RemedyFindingState.Evaluated)));

        Assert.Equal(RemedyOutcome.UnknownCode, resolution.Outcome);
        Assert.NotNull(resolution.Reason);
        Assert.Equal(RemedyReason.CatalogueStale, resolution.Reason);
    }

    [Fact]
    public void A_code_whose_finding_was_never_assessed_is_not_evaluated_not_unknown()
    {
        // The distinction that matters: the finding EXISTS, so this is not a stale catalogue entry. Reporting it as
        // UnknownCode would send a maintainer to check the catalogue when the real cause is missing birth data.
        var resolution = RemedyResolver.Resolve("dosha:mangal", Findings(("dosha:mangal", RemedyFindingState.NotEvaluated)));

        Assert.Equal(RemedyOutcome.NotEvaluated, resolution.Outcome);
        Assert.NotNull(resolution.Reason);
        Assert.Equal(RemedyReason.InputMissing, resolution.Reason);
        Assert.NotEqual(RemedyReason.CatalogueStale, resolution.Reason);
    }

    [Fact]
    public void A_code_whose_finding_is_withheld_is_withheld_not_not_evaluated()
    {
        // Withheld means the engine looked and the publication is held back; NotEvaluated means it never looked.
        // Collapsing them would hide a capability gap behind an input problem, or the reverse.
        var resolution = RemedyResolver.Resolve("dosha:mangal", Findings(("dosha:mangal", RemedyFindingState.Withheld)));

        Assert.Equal(RemedyOutcome.Withheld, resolution.Outcome);
        Assert.NotNull(resolution.Reason);
        Assert.Equal(RemedyReason.CapabilityGap, resolution.Reason);
    }

    [Fact]
    public void The_three_refusals_are_three_distinct_outcomes_with_three_distinct_reasons()
    {
        var findings = Findings(
            ("never", RemedyFindingState.NotEvaluated),
            ("held", RemedyFindingState.Withheld));

        var unknown = RemedyResolver.Resolve("absent", findings);
        var notEvaluated = RemedyResolver.Resolve("never", findings);
        var withheld = RemedyResolver.Resolve("held", findings);

        var outcomes = new[] { unknown.Outcome, notEvaluated.Outcome, withheld.Outcome };
        Assert.Equal(3, outcomes.Distinct().Count());

        var reasons = new[] { unknown.Reason, notEvaluated.Reason, withheld.Reason };
        Assert.Equal(3, reasons.Distinct().Count());
        // Every non-attached outcome names its own cause, and the enum makes "named" a type-level fact rather than a
        // non-empty-string check - the three are distinct members, not three strings that happen to differ.
        Assert.All(reasons, reason => Assert.NotNull(reason));
        Assert.Equal(3, reasons.Select(RemedyReason? (r) => r).Distinct().Count());
    }

    // ---- the attaching case, proved positively ----

    [Fact]
    public void A_code_that_resolves_to_an_evaluated_finding_attaches_and_needs_no_reason()
    {
        var resolution = RemedyResolver.Resolve("yoga:gaja-kesari",
            Findings(("yoga:gaja-kesari", RemedyFindingState.Evaluated)));

        Assert.Equal(RemedyOutcome.Attached, resolution.Outcome);
        // Attached is the only outcome that supplies no reason, because attachment needs no explanation. This mirrors
        // KundaliChartPrecision: the reason is absent for the case whose status means "nothing to explain".
        Assert.Null(resolution.Reason);
        Assert.Equal("yoga:gaja-kesari", resolution.FindingCode);
    }

    [Fact]
    public void An_empty_finding_set_attaches_nothing()
    {
        // Default-deny at the boundary: no findings means every remedy is UnknownCode, never Attached.
        var resolution = RemedyResolver.Resolve("yoga:gaja-kesari", Findings());

        Assert.Equal(RemedyOutcome.UnknownCode, resolution.Outcome);
    }

    [Fact]
    public void A_blank_finding_code_is_refused_rather_than_resolved()
    {
        // A remedy with no code cannot be attributed to anything. Refusing it is honest; resolving it to an outcome
        // would invent a state for a row that has no identity.
        Assert.ThrowsAny<ArgumentException>(() => RemedyResolver.Resolve("  ", Findings()));
    }

    [Fact]
    public void Resolution_is_ordinal_so_a_case_variant_does_not_silently_attach()
    {
        var findings = Findings(("yoga:Gaja-Kesari", RemedyFindingState.Evaluated));

        Assert.Equal(RemedyOutcome.UnknownCode,
            RemedyResolver.Resolve("yoga:gaja-kesari", findings).Outcome);
    }

    // ---- THE ENFORCEMENT of the closed set. Not coverage of one branch - this is the only thing that holds it. ----

    [Fact]
    public void An_undefined_finding_state_cannot_reach_an_attached_outcome()
    {
        // There is no compiler diagnostic available to enforce this here, so it is enforced by a test - and this one,
        // not some other. Measured twice: adding a fifth member to RemedyFindingState produced 0 errors and 0 warnings,
        // both with a discard arm and with it removed. CS8509 needs a switch that is non-exhaustive AND has no discard
        // arm; without "_" the expression is simply non-exhaustive and the compiler stays silent in this configuration.
        //
        // Consequence: if this test is deleted or weakened, nothing else catches a fifth state reaching the switch, and
        // an undefined value would fall to whatever the runtime does with a non-exhaustive expression. Treat it as the
        // guarantee it is rather than as redundant coverage of the refusal arm.
        var findings = Findings(("dosha:mangal", (RemedyFindingState)999));

        var failure = Assert.Throws<ArgumentOutOfRangeException>(() =>
            RemedyResolver.Resolve("dosha:mangal", findings));

        // Compared against the producer's own constant, not a transcription of it: rewording the message cannot leave
        // this assertion passing against a string nothing emits.
        //
        // Contains rather than Equal because ArgumentOutOfRangeException appends " (Parameter 'findingCode')" to the
        // message it was constructed with - so an equality assertion here would fail on framework formatting rather
        // than on the guard, and would have to hard-code the suffix to pass, which is the transcription this constant
        // exists to avoid.
        Assert.Contains(MyVedicStar.Kundali.Business.Reasons.UnrecognisedStateMessage, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_declared_outcome_set_is_exactly_the_ones_this_design_names()
    {
        // Pins the CLOSED SET, which nothing did before: RemedyOutcome was mentioned in tests but never enumerated, so a
        // fifth member could be added with every test green. RemedyReason already had this pin; RemedyOutcome did not.
        //
        // Two properties, deliberately:
        //  - ADDITION: Enum.GetValues sees a new member, so the explicit expected list fails on it.
        //  - REMOVAL: the expected list is written out, not derived from the enum, so a removed member still fails.
        // A pin that only iterated the enum would catch growth and silently allow shrinkage - half a pin.
        //
        // The assertion compares NAMES via the explicit list rather than the collection count, so a failure names the
        // member that is missing or unexpected instead of reporting a length mismatch.
        var declared = Enum.GetValues<RemedyOutcome>().Select(outcome => outcome.ToString()).ToArray();

        Assert.Equal(
            ["Unspecified", "Attached", "NotEvaluated", "Withheld", "UnknownCode", "CatalogueMissing"],
            declared);
    }

    [Fact]
    public void The_reserved_zero_outcome_is_not_the_renderable_one()
    {
        // default(RemedyOutcome) is what an uninitialised variable holds. If that were Attached, a bug elsewhere could
        // render a remedy by accident. Zero is a reserved non-outcome instead.
        Assert.NotEqual(RemedyOutcome.Attached, default(RemedyOutcome));
        Assert.Equal(RemedyOutcome.Unspecified, default(RemedyOutcome));
    }

    // ---- stored payloads: same outcome, different cause ----

    [Fact]
    public void An_unreadable_stored_status_says_the_payload_is_unreadable_not_that_the_catalogue_is_stale()
    {
        // Both refusals land on UnknownCode, so the outcome alone cannot tell a maintainer what to fix. The reason can:
        // a stale catalogue is fixed by editing the catalogue, an unreadable payload by looking at the stored revision.
        var fromCatalogue = RemedyResolver.Resolve("dosha:mangal", Findings());
        var fromPayload = RemedyResolver.ResolveStored("dosha:mangal", "some-retired-status", Findings());

        Assert.Equal(fromCatalogue.Outcome, fromPayload.Outcome);
        Assert.Equal(RemedyReason.CatalogueStale, fromCatalogue.Reason);
        Assert.Equal(RemedyReason.StoredStatusUnreadable, fromPayload.Reason);
        Assert.NotEqual(fromCatalogue.Reason, fromPayload.Reason);
    }

    [Fact]
    public void A_stored_evaluated_status_does_not_attach_by_itself()
    {
        // The stored text is a claim by the build that produced it, not evidence about the current engine. If the
        // finding is gone now, the remedy still does not attach.
        var resolution = RemedyResolver.ResolveStored("dosha:mangal", "evaluated", Findings());

        Assert.Equal(RemedyOutcome.UnknownCode, resolution.Outcome);
        Assert.Equal(RemedyReason.CatalogueStale, resolution.Reason);
    }

    [Fact]
    public void A_readable_stored_status_defers_to_the_current_findings()
    {
        var findings = Findings(("dosha:mangal", RemedyFindingState.Withheld));
        var resolution = RemedyResolver.ResolveStored("dosha:mangal", "evaluated", findings);

        // The payload said evaluated; the engine now withholds it. The current state wins.
        Assert.Equal(RemedyOutcome.Withheld, resolution.Outcome);
    }

}

