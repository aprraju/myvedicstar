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
        Assert.Contains("stale or superseded", resolution.Reason);
    }

    [Fact]
    public void A_code_whose_finding_was_never_assessed_is_not_evaluated_not_unknown()
    {
        // The distinction that matters: the finding EXISTS, so this is not a stale catalogue entry. Reporting it as
        // UnknownCode would send a maintainer to check the catalogue when the real cause is missing birth data.
        var resolution = RemedyResolver.Resolve("dosha:mangal", Findings(("dosha:mangal", RemedyFindingState.NotEvaluated)));

        Assert.Equal(RemedyOutcome.NotEvaluated, resolution.Outcome);
        Assert.NotNull(resolution.Reason);
        Assert.Contains("input was missing", resolution.Reason);
        Assert.DoesNotContain("stale", resolution.Reason);
    }

    [Fact]
    public void A_code_whose_finding_is_withheld_is_withheld_not_not_evaluated()
    {
        // Withheld means the engine looked and the publication is held back; NotEvaluated means it never looked.
        // Collapsing them would hide a capability gap behind an input problem, or the reverse.
        var resolution = RemedyResolver.Resolve("dosha:mangal", Findings(("dosha:mangal", RemedyFindingState.Withheld)));

        Assert.Equal(RemedyOutcome.Withheld, resolution.Outcome);
        Assert.NotNull(resolution.Reason);
        Assert.Contains("not published", resolution.Reason);
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
        Assert.All(reasons, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
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
}

