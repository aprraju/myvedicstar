using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// KUN-14 (api#54): the interpretation rule must strip what cannot be attributed, refuse what the product will not say,
/// and never let "withheld" and "not evaluated" become the same statement.
/// </summary>
public sealed class InterpretationRuleTests
{
    private static readonly Dictionary<string, KundaliInterpretationFactor> Known = new(StringComparer.Ordinal)
    {
        ["graha:saturn:sign"] = new("graha:saturn:sign", "Saturn is in Libra"),
        ["graha:jupiter:sign"] = new("graha:jupiter:sign", "Jupiter is in Sagittarius"),
    };

    [Fact]
    public void A_draft_citing_only_known_factors_is_ready()
    {
        var verdict = InterpretationRule.Apply(KundaliInterpretationDomains.Career,
            new InterpretationDraft("Steady effort suits you.", ["graha:saturn:sign"]), Known);

        Assert.Equal(KundaliInterpretationStatuses.Ready, verdict.Status);
        Assert.Null(verdict.Reason);
        Assert.Single(verdict.Factors);
        Assert.Empty(verdict.UnknownFactorReferences);
    }

    [Fact]
    public void An_unmatched_factor_id_withholds_the_domain_and_is_reported()
    {
        // The refinement's stated test: a stubbed model returning an unmatched factor id must not be published as if it
        // were evidence.
        var verdict = InterpretationRule.Apply(KundaliInterpretationDomains.Marriage,
            new InterpretationDraft("Venus smiles on you.", ["graha:venus:sign"]), Known);

        Assert.Equal(KundaliInterpretationStatuses.Withheld, verdict.Status);
        Assert.Null(verdict.Text);
        Assert.Equal(KundaliInterpretationReasons.NoAttributableFactors, verdict.Reason);
        Assert.Equal(["graha:venus:sign"], verdict.UnknownFactorReferences);
    }

    [Fact]
    public void A_partially_attributable_draft_keeps_only_the_known_factors_and_reports_the_rest()
    {
        var verdict = InterpretationRule.Apply(KundaliInterpretationDomains.Finance,
            new InterpretationDraft("Growth with patience.", ["graha:jupiter:sign", "house:second"]), Known);

        Assert.Equal(KundaliInterpretationStatuses.Ready, verdict.Status);
        Assert.Single(verdict.Factors);
        Assert.Equal("graha:jupiter:sign", verdict.Factors[0].Id);
        Assert.Equal(["house:second"], verdict.UnknownFactorReferences);
    }

    [Theory]
    [InlineData("This placement may cause a chronic illness you should be diagnosed for.")]
    [InlineData("Your chart guarantees wealth by forty.")]
    [InlineData("A prescribed remedy will cure the difficulty.")]
    public void Prescriptive_or_guaranteeing_text_is_withheld(string text)
    {
        var verdict = InterpretationRule.Apply(KundaliInterpretationDomains.Wellbeing,
            new InterpretationDraft(text, ["graha:saturn:sign"]), Known);

        Assert.Equal(KundaliInterpretationStatuses.Withheld, verdict.Status);
        Assert.Equal(KundaliInterpretationReasons.ContentRejected, verdict.Reason);
        Assert.Null(verdict.Text);
    }

    [Fact]
    public void An_empty_text_is_withheld_rather_than_published_as_blank()
    {
        var verdict = InterpretationRule.Apply(KundaliInterpretationDomains.Education,
            new InterpretationDraft("   ", ["graha:saturn:sign"]), Known);

        Assert.Equal(KundaliInterpretationStatuses.Withheld, verdict.Status);
        Assert.Equal(KundaliInterpretationReasons.NoAttributableFactors, verdict.Reason);
    }

    [Fact]
    public void Withheld_and_not_evaluated_are_distinguishable_reasons()
    {
        var withheld = InterpretationRule.Withheld(KundaliInterpretationDomains.Wellbeing,
            KundaliInterpretationReasons.CapabilityGap);
        var notEvaluated = InterpretationRule.NotEvaluated(KundaliInterpretationDomains.Wellbeing,
            KundaliInterpretationReasons.InputMissing);

        Assert.NotEqual(withheld.Status, notEvaluated.Status);
        Assert.NotEqual(withheld.Reason, notEvaluated.Reason);
        Assert.Equal(KundaliInterpretationStatuses.Withheld, withheld.Status);
        Assert.Equal(KundaliInterpretationStatuses.NotEvaluated, notEvaluated.Status);
    }

    [Fact]
    public void An_unknown_domain_is_rejected_rather_than_defaulted()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InterpretationRule.Apply("romance",
            new InterpretationDraft("text", ["graha:saturn:sign"]), Known));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InterpretationRule.NotEvaluated("romance", KundaliInterpretationReasons.InputMissing));
    }

    [Fact]
    public void Duplicate_factor_ids_are_declared_once()
    {
        var verdict = InterpretationRule.Apply(KundaliInterpretationDomains.Career,
            new InterpretationDraft("Text.", ["graha:saturn:sign", "graha:saturn:sign"]), Known);

        Assert.Single(verdict.Factors);
    }
}

/// <summary>The factor set is built from published positions only: an unpublished graha contributes nothing, and an
/// empty set is what makes a domain <c>not-evaluated</c> instead of narrated.</summary>
public sealed class InterpretationFactorTests
{
    [Fact]
    public void Factors_are_namespaced_and_skip_unpublished_positions()
    {
        var factors = InterpretationFactors.For(KundaliInterpretationDomains.Career,
        [
            ("Saturn", "Libra"),
            ("Rahu", null),
            ("Sun", "Aries"),
        ]);

        Assert.Equal(["graha:saturn:sign", "graha:sun:sign"], factors.Select(f => f.Id).ToArray());
        Assert.Equal("Saturn is in Libra", factors[0].Description);
    }

    [Fact]
    public void An_unknown_domain_is_rejected_by_the_factor_builder_too()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InterpretationFactors.For("romance", [("Saturn", "Libra")]));
    }

    [Fact]
    public void Varshaphal_has_exactly_one_shape_and_it_says_not_available()
    {
        // The engine has no annual chart. Asserted so a future change cannot turn the not-available branch into a payload
        // of empty-but-present annual facts.
        var section = KundaliVarshaphalSection.NotAvailable;

        Assert.False(section.Available);
        Assert.Equal(KundaliVarshaphalReasons.EngineHasNoAnnualChart, section.Reason);
    }
}
