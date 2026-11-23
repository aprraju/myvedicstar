using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// KUN-14 (api#54): the payload shape must ENUMERATE every domain with its own status and reason, and must keep
/// "assessed and failed" distinguishable from "never assessed" as data rather than as prose.
/// </summary>
public sealed class InterpretationSectionTests
{
    private static readonly IReadOnlyDictionary<string, KundaliInterpretationFactor> Known =
        new Dictionary<string, KundaliInterpretationFactor>(StringComparer.Ordinal)
        {
            ["graha:saturn:sign"] = new("graha:saturn:sign", "Saturn is in Libra"),
        };

    private static InterpretationVerdict Ready(string domain) =>
        InterpretationRule.Apply(domain, new InterpretationDraft("Steady effort suits you.", ["graha:saturn:sign"]), Known);

    [Fact]
    public void Every_domain_is_enumerated_even_when_only_one_was_evaluated()
    {
        var verdicts = new Dictionary<string, InterpretationVerdict>(StringComparer.Ordinal)
        {
            [KundaliInterpretationDomains.Career] = Ready(KundaliInterpretationDomains.Career),
        };

        var section = InterpretationSection.Compose(7, "en", "p1", "no-model", verdicts);

        Assert.Equal(KundaliInterpretationDomains.All.Count, section.Sections.Count);
        Assert.Equal(KundaliInterpretationDomains.All, section.Sections.Select(entry => entry.Domain).ToArray());
        Assert.Single(section.Sections, entry => entry.Status == KundaliInterpretationStatuses.Ready);
    }

    [Fact]
    public void A_domain_with_no_verdict_is_not_evaluated_with_its_own_reason_and_never_omitted()
    {
        var section = InterpretationSection.Compose(7, "en", "p1", "no-model",
            new Dictionary<string, InterpretationVerdict>(StringComparer.Ordinal),
            fallbackReason: KundaliInterpretationReasons.AiDisabled);

        Assert.All(section.Sections, entry =>
        {
            Assert.Equal(KundaliInterpretationStatuses.NotEvaluated, entry.Status);
            Assert.Equal(KundaliInterpretationReasons.AiDisabled, entry.Reason);
            Assert.Null(entry.Text);
        });
    }

    [Fact]
    public void Assessed_and_failed_is_a_different_status_from_never_assessed()
    {
        var verdicts = new Dictionary<string, InterpretationVerdict>(StringComparer.Ordinal)
        {
            [KundaliInterpretationDomains.Wellbeing] = InterpretationRule.Withheld(
                KundaliInterpretationDomains.Wellbeing, KundaliInterpretationReasons.ContentRejected),
        };

        var section = InterpretationSection.Compose(7, "en", "p1", "model-x", verdicts,
            fallbackReason: KundaliInterpretationReasons.AiDisabled);

        var withheld = section.Sections.Single(entry => entry.Domain == KundaliInterpretationDomains.Wellbeing);
        var never = section.Sections.Single(entry => entry.Domain == KundaliInterpretationDomains.Finance);

        Assert.Equal(KundaliInterpretationStatuses.Withheld, withheld.Status);
        Assert.Equal(KundaliInterpretationReasons.ContentRejected, withheld.Reason);
        Assert.Equal(KundaliInterpretationStatuses.NotEvaluated, never.Status);
        Assert.Equal(KundaliInterpretationReasons.AiDisabled, never.Reason);
        Assert.NotEqual(withheld.Status, never.Status);
    }

    [Fact]
    public void Every_entry_carries_a_reason_unless_it_is_ready()
    {
        var verdicts = new Dictionary<string, InterpretationVerdict>(StringComparer.Ordinal)
        {
            [KundaliInterpretationDomains.Career] = Ready(KundaliInterpretationDomains.Career),
            [KundaliInterpretationDomains.Education] = InterpretationRule.NotEvaluated(
                KundaliInterpretationDomains.Education, KundaliInterpretationReasons.InputMissing),
        };

        var section = InterpretationSection.Compose(7, "en", "p1", "no-model", verdicts);

        foreach (var entry in section.Sections)
        {
            if (entry.Status == KundaliInterpretationStatuses.Ready)
            {
                Assert.Null(entry.Reason);
                Assert.NotNull(entry.Text);
                Assert.NotEmpty(entry.Factors);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Reason));
                Assert.Null(entry.Text);
            }
        }
    }

    [Fact]
    public void A_verdict_filed_under_the_wrong_domain_is_refused()
    {
        var verdicts = new Dictionary<string, InterpretationVerdict>(StringComparer.Ordinal)
        {
            [KundaliInterpretationDomains.Career] = Ready(KundaliInterpretationDomains.Marriage),
        };

        Assert.Throws<InvalidOperationException>(() =>
            InterpretationSection.Compose(7, "en", "p1", "no-model", verdicts));
    }

    [Fact]
    public void The_section_carries_its_label_provenance_and_revision()
    {
        var section = InterpretationSection.Compose(42, "en-IN", "prompt-v1", "anthropic/claude-x",
            new Dictionary<string, InterpretationVerdict>(StringComparer.Ordinal));

        Assert.Equal(42, section.SourceRevision);
        Assert.Equal("prompt-v1", section.PromptVersion);
        Assert.Equal("anthropic/claude-x", section.ModelProvenance);
        Assert.Equal("en-IN", section.Locale);
        Assert.Equal(InterpretationSection.TraditionalLabel, section.InterpretationLabel);
    }

    [Fact]
    public void Varshaphal_is_always_the_not_available_branch()
    {
        var varshaphal = InterpretationSection.Varshaphal();

        Assert.False(varshaphal.Available);
        Assert.Equal(KundaliVarshaphalReasons.EngineHasNoAnnualChart, varshaphal.Reason);
    }

    [Fact]
    public void Interpretations_is_in_the_published_section_set()
    {
        // The payload is requested through the report's closed section set, so this asserts the two cannot drift.
        Assert.Contains(KundaliSections.Interpretations, KundaliSections.All);
        Assert.Contains(KundaliSections.Interpretations, KundaliCapabilities.Report().ReportSections);
    }
}
