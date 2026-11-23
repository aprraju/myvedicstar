using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;

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
            missingDomainReasons: KundaliInterpretationDomains.All.ToDictionary(
                domain => domain,
                _ => KundaliInterpretationReasons.AiDisabled,
                StringComparer.Ordinal));

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
            missingDomainReasons: KundaliInterpretationDomains.All.ToDictionary(
                domain => domain,
                _ => KundaliInterpretationReasons.AiDisabled,
                StringComparer.Ordinal));

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

    [Fact]
    public void Missing_domains_take_their_own_reason_not_one_blanket_value()
    {
        // The review point: one reason for every missing domain would have to be the lowest common denominator. Here
        // Finance is missing because the provider is off, Marriage because inputs are absent - two different facts.
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KundaliInterpretationDomains.Finance] = KundaliInterpretationReasons.AiDisabled,
            [KundaliInterpretationDomains.Marriage] = KundaliInterpretationReasons.InputMissing,
        };

        var section = InterpretationSection.Compose(7, "en", "p1", "no-model",
            new Dictionary<string, InterpretationVerdict>(StringComparer.Ordinal), reasons);

        Assert.Equal(KundaliInterpretationReasons.AiDisabled,
            section.Sections.Single(entry => entry.Domain == KundaliInterpretationDomains.Finance).Reason);
        Assert.Equal(KundaliInterpretationReasons.InputMissing,
            section.Sections.Single(entry => entry.Domain == KundaliInterpretationDomains.Marriage).Reason);
        // A domain with no supplied reason still gets the honest default rather than an empty field.
        Assert.Equal(KundaliInterpretationReasons.InputMissing,
            section.Sections.Single(entry => entry.Domain == KundaliInterpretationDomains.Career).Reason);
    }

    [Fact]
    public void Unknown_factor_references_are_reachable_and_survive_into_the_payload()
    {
        // The review point: an entry carries UnknownFactorReferences, so the case must be reachable and asserted -
        // a field that can only ever be empty is a robustness claim nothing exercises.
        var verdict = InterpretationRule.Apply(KundaliInterpretationDomains.Career,
            new InterpretationDraft("Steady effort suits you.", ["graha:saturn:sign", "graha:venus:sign"]), Known);

        var section = InterpretationSection.Compose(7, "en", "p1", "model-x",
            new Dictionary<string, InterpretationVerdict>(StringComparer.Ordinal)
            {
                [KundaliInterpretationDomains.Career] = verdict,
            });

        var career = section.Sections.Single(entry => entry.Domain == KundaliInterpretationDomains.Career);
        Assert.Equal(KundaliInterpretationStatuses.Ready, career.Status);
        Assert.Equal(["graha:venus:sign"], career.UnknownFactorReferences);
        Assert.Single(career.Factors);
    }
}

/// <summary>
/// KUN-14 facade slice (api#54): the business path must narrate only the requested domain, enumerate the rest with a
/// stated reason, reject an unknown domain, and hand the narrator only published positions.
/// </summary>
public sealed class KundaliInterpretationsFacadeTests
{
    private static readonly Guid ProfileId = Guid.NewGuid();

    private sealed class StubNarrator : IKundaliInterpretationNarrator
    {
        public bool IsEnabled => true;
        public string Fingerprint => "test/model";
        public string PromptVersion => "prompt-test";
        public string? LastDomain { get; private set; }
        public IReadOnlyList<KundaliInterpretationFactor> LastFactors { get; private set; } = [];
        public InterpretationVerdict? Result { get; set; }

        public Task<InterpretationVerdict> NarrateAsync(string domain, string locale, long sourceRevision,
            IReadOnlyList<KundaliInterpretationFactor> factors, CancellationToken token)
        {
            LastDomain = domain;
            LastFactors = factors;
            return Task.FromResult(Result ?? InterpretationRule.Apply(domain,
                new InterpretationDraft("Steady effort suits you.", factors.Select(factor => factor.Id).ToArray()),
                factors.ToDictionary(factor => factor.Id, StringComparer.Ordinal)));
        }
    }

    /// <summary>
    /// Builds the facade the same way the other Kundali tests do, with the publishable and the withheld positions
    /// present in one engine result so the factor filter is exercised rather than assumed.
    /// </summary>
    /// <summary>One principal instance shared by the stubs and the call, so the arrangement and the invocation cannot
    /// disagree about who is asking.</summary>
    private static readonly ClaimsPrincipal SharedUser =
        new(new ClaimsIdentity([new Claim("sub", "owner")], "test"));

    private static IKundaliFacade Facade(StubNarrator narrator)
    {
        var profiles = Substitute.For<IKundaliProfileReader>();
        var repository = Substitute.For<IKundaliRepository>();
        var engine = Substitute.For<IKundaliCalculationEngine>();
        var user = SharedUser;

        profiles.GetDisplayAsync(user, ProfileId, Arg.Any<CancellationToken>())
            .Returns(new KundaliProfileDisplay("Test", new DateOnly(1990, 5, 4), BirthTimeMode.Known,
                new TimeOnly(6, 30), null, null, "Chennai", 13.0827m, 80.2707m, "Asia/Kolkata"));
        profiles.GetSnapshotAsync(user, ProfileId, Arg.Any<CancellationToken>())
            .Returns(new KundaliInputSnapshot(new DateOnly(1990, 5, 4), BirthTimeMode.Known,
                new TimeOnly(6, 30), null, null, 13.0827m, 80.2707m, "Asia/Kolkata", 11));
        engine.Calculate(Arg.Any<KundaliEngineInput>()).Returns(new KundaliEngineResult(
            SiderealSunLongitude: null, SiderealMoonLongitude: null, SunSign: null, MoonSign: null,
            VimshottariMahadashas: null, YoginiPeriods: null, CharaSignPeriods: null, Gaps: [],
            GrahaPositions:
            [
                new KundaliEngineGrahaPosition("Saturn", 190.0, "Libra", null),
                new KundaliEngineGrahaPosition("Jupiter", 250.0, "Sagittarius", null),
                // Withheld: no longitude. It must never reach the narrator as a factor.
                new KundaliEngineGrahaPosition("Rahu", null, null, "unvalidated-reference"),
            ]));

        var provider = KundaliTestProvider.Create()
            .With(profiles)
            .With(repository)
            .With<IKundaliCalculationEngine>(engine)
            .With<IKundaliInterpretationNarrator>(narrator)
            .With(Substitute.For<IKundaliResultCache>())
            .With<IKundaliAshtakavargaRules>(new UnregisteredAshtakavargaRules())
            .With<IKundaliShadbalaRules>(new UnregisteredShadbalaRules())
            .With<IKundaliYogaRules>(new UnregisteredYogaRules())
            .Build();

        return provider.CreateScope().ServiceProvider.GetRequiredService<IKundaliFacade>();
    }

    private static ClaimsPrincipal User() => SharedUser;

    [Fact]
    public async Task Only_the_requested_domain_is_narrated_and_the_rest_say_not_requested()
    {
        var narrator = new StubNarrator();
        var facade = Facade(narrator);

        var section = await facade.GetInterpretationsAsync(User(), ProfileId,
            KundaliInterpretationDomains.Career, CancellationToken.None);

        Assert.Equal(KundaliInterpretationDomains.Career, narrator.LastDomain);
        Assert.Equal(KundaliInterpretationDomains.All.Count, section.Sections.Count);
        Assert.Equal(KundaliInterpretationStatuses.Ready,
            section.Sections.Single(entry => entry.Domain == KundaliInterpretationDomains.Career).Status);

        foreach (var other in section.Sections.Where(entry => entry.Domain != KundaliInterpretationDomains.Career))
        {
            Assert.Equal(KundaliInterpretationStatuses.NotEvaluated, other.Status);
            Assert.Equal(KundaliInterpretationReasons.NotRequested, other.Reason);
        }
    }

    [Fact]
    public async Task An_unknown_domain_is_a_validation_error_not_a_default()
    {
        var facade = Facade(new StubNarrator());

        await Assert.ThrowsAsync<ValidationException>(() =>
            facade.GetInterpretationsAsync(User(), ProfileId, "romance", CancellationToken.None));
    }

    [Fact]
    public async Task A_withheld_position_never_reaches_the_narrator_as_a_factor()
    {
        var narrator = new StubNarrator();
        var facade = Facade(narrator);

        await facade.GetInterpretationsAsync(User(), ProfileId, KundaliInterpretationDomains.Career,
            CancellationToken.None);

        Assert.Equal(2, narrator.LastFactors.Count);
        Assert.DoesNotContain(narrator.LastFactors, factor => factor.Id.Contains("rahu", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_withheld_verdict_stays_in_the_payload_with_its_own_reason()
    {
        var narrator = new StubNarrator
        {
            Result = InterpretationRule.Withheld(KundaliInterpretationDomains.Career,
                KundaliInterpretationReasons.ContentRejected),
        };
        var facade = Facade(narrator);

        var section = await facade.GetInterpretationsAsync(User(), ProfileId, KundaliInterpretationDomains.Career,
            CancellationToken.None);

        var career = section.Sections.Single(entry => entry.Domain == KundaliInterpretationDomains.Career);
        Assert.Equal(KundaliInterpretationStatuses.Withheld, career.Status);
        Assert.Equal(KundaliInterpretationReasons.ContentRejected, career.Reason);
        Assert.Null(career.Text);
        Assert.Equal(KundaliInterpretationDomains.All.Count, section.Sections.Count);
    }

    [Fact]
    public async Task The_section_carries_the_narrators_prompt_version_and_provenance()
    {
        var facade = Facade(new StubNarrator());

        var section = await facade.GetInterpretationsAsync(User(), ProfileId,
            KundaliInterpretationDomains.Finance, CancellationToken.None);

        Assert.Equal("prompt-test", section.PromptVersion);
        Assert.Equal("test/model", section.ModelProvenance);
        Assert.Equal(11, section.SourceRevision);
    }

    [Fact]
    public async Task An_absent_domain_means_no_filter_and_narrates_nothing()
    {
        // The plain route must be well defined: absent is not "unknown". No domain is narrated, every domain is
        // enumerated with its own reason, and no 400 is thrown.
        var narrator = new StubNarrator();
        var facade = Facade(narrator);

        var section = await facade.GetInterpretationsAsync(User(), ProfileId, domain: null, CancellationToken.None);

        Assert.Null(narrator.LastDomain);
        Assert.Equal(KundaliInterpretationDomains.All.Count, section.Sections.Count);
        Assert.All(section.Sections, entry =>
        {
            Assert.Equal(KundaliInterpretationStatuses.NotEvaluated, entry.Status);
            Assert.Equal(KundaliInterpretationReasons.NotRequested, entry.Reason);
        });
    }

    [Fact]
    public async Task A_blank_domain_is_treated_as_absent_not_as_an_error()
    {
        var facade = Facade(new StubNarrator());

        var section = await facade.GetInterpretationsAsync(User(), ProfileId, "   ", CancellationToken.None);

        Assert.Equal(KundaliInterpretationDomains.All.Count, section.Sections.Count);
    }
}
