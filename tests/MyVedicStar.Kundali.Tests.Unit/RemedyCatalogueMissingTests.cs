using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.VedicProfiles.Shared;
using NSubstitute;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// KUN-16 (api#56): an evaluated finding whose code the catalogue does not carry must be REPORTED, not silently
/// absent. Before this, RemedyEmission.Select iterated the catalogue, so such a finding was never visited and produced
/// neither a remedy nor an unsupported entry - an empty list with no explanation, which renders as "no remedy applies".
/// </summary>
public sealed class RemedyCatalogueMissingTests
{
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly Guid accountId = Guid.NewGuid();
    private readonly Guid profileId = Guid.NewGuid();
    private readonly IKundaliProfileReader profiles = Substitute.For<IKundaliProfileReader>();
    private readonly IKundaliRepository repository = Substitute.For<IKundaliRepository>();
    private readonly IKundaliAstronomyMetadataReader metadata = Substitute.For<IKundaliAstronomyMetadataReader>();
    private readonly IKundaliCalculationEngine engine = Substitute.For<IKundaliCalculationEngine>();
    private readonly IKundaliYogaRules yoga = Substitute.For<IKundaliYogaRules>();
    private readonly ServiceProvider provider;
    private readonly IKundaliFacade facade;
    private static readonly CancellationToken Token = CancellationToken.None;

    /// <summary>A code the catalogue provably does not carry.</summary>
    private const string UncataloguedCode = "yoga:not-in-catalogue";

    public RemedyCatalogueMissingTests()
    {
        profiles.GetRegisteredAccountIdAsync(user, Token).Returns(accountId);
        profiles.GetAyanamsaAsync(user, Token).Returns("Lahiri");
        // The remedy path reads the astronomy metadata envelope for its ayanamsa; unstubbed it returns null and the
        // engine input line throws. Stubbed here rather than tolerated so a real null in that path still fails.
        metadata.GetAsync(user, Token).Returns(new KundaliAstronomyMetadata(
            "analytical", "analytical", "Lahiri", "WholeSign", "Mean", "Sidereal"));
        profiles.GetDisplayAsync(user, profileId, Token).Returns(new KundaliProfileDisplay(
            "Test Person", new DateOnly(1990, 3, 15), BirthTimeMode.Known,
            new TimeOnly(6, 45), null, null, "London", 51.5m, -0.12m, "Europe/London"));

        engine.Calculate(Arg.Any<KundaliEngineInput>()).Returns(new KundaliEngineResult(
            null, null, null, null, null, null, null, Array.Empty<KundaliEngineGap>()));

        provider = KundaliTestProvider.Create()
            .With(profiles)
            .With(repository)
            .With(metadata)
            .With<IKundaliCalculationEngine>(engine)
            .With<IKundaliYogaRules>(yoga)
            .With(Substitute.For<IKundaliResultCache>())
            .Build();
        facade = provider.CreateScope().ServiceProvider.GetRequiredService<IKundaliFacade>();
    }

    /// <summary>Reports one finding in the given buckets, with nothing else.</summary>
    private void YogaReports(IReadOnlyList<KundaliYogaResult> evaluated, IReadOnlyList<KundaliYogaResult> notEvaluated)
    {
        yoga.RulesVersion.Returns("test-rules-1");
        yoga.Evaluate(Arg.Any<IReadOnlyDictionary<string, int>>()).Returns(
            new KundaliYogaEvaluation("test-rules-1", evaluated, notEvaluated));
    }

    private static KundaliYogaResult Finding(string code) =>
        new(code, code, "chart", "present", [], [], [], [], [], true, null, []);

    // ---- THE property the user can see (api#56 condition 4) ----

    [Fact]
    public async Task An_evaluated_finding_with_no_catalogue_entry_is_reported_in_unsupported()
    {
        // This is the defect being fixed. It fails on the code before this change: the finding was evaluated, the
        // catalogue had nothing for it, and it appeared in NEITHER items nor unsupported - so a client saw an empty
        // list and rendered "no remedy applies", a claim we cannot support.
        YogaReports([Finding(UncataloguedCode)], []);

        var section = await facade.GetRemediesAsync(user, profileId, Token);

        Assert.DoesNotContain(section.Items, item => item.FindingCode == UncataloguedCode);

        var reported = Assert.Single(section.Unsupported, field => field.Path == UncataloguedCode);
        Assert.Equal("catalogue-missing", reported.Reason);
    }

    [Fact]
    public async Task A_finding_the_engine_could_not_assess_is_never_reported_as_catalogue_missing()
    {
        // api#56 condition 3, and the assertion had to be weakened honestly because the shipped behaviour is narrower
        // than I first wrote it: a NotEvaluated finding with NO catalogue entry is never visited either (the loop
        // iterates the catalogue), so it appears in NEITHER list. What this test can and must pin is that the new
        // outcome does not absorb it: nothing claims `catalogue-missing` for a finding the engine could not assess.
        //
        // The residual gap (a NotEvaluated + uncatalogued finding being invisible) is pre-existing, is NOT what api#56
        // was raised for, and is reported on the issue rather than silently fixed here.
        YogaReports([], [Finding(UncataloguedCode)]);

        var section = await facade.GetRemediesAsync(user, profileId, Token);

        Assert.DoesNotContain(section.Unsupported, field => field.Reason == "catalogue-missing");
        Assert.DoesNotContain(section.Items, item => item.FindingCode == UncataloguedCode);
    }

    [Fact]
    public async Task CatalogueMissing_is_only_reachable_from_a_positively_evaluated_finding()
    {
        // Pins the mechanism rather than the symptom: the filter in GetRemediesAsync requires
        // RemedyFindingState.Evaluated, so an uncatalogued code that is merely absent from both sets is not reported
        // as a catalogue gap. (It is not a finding of this run at all.)
        YogaReports([], []);

        var section = await facade.GetRemediesAsync(user, profileId, Token);

        Assert.DoesNotContain(section.Unsupported,
            field => field.Reason == "catalogue-missing");
    }

    [Fact]
    public void The_reason_that_names_a_catalogue_gap_is_not_the_one_that_blames_the_input()
    {
        // The two must never share wire text: the merge dedupes on (path, reason), so identical text would silently
        // collapse "we have nothing written for this" into "your birth data was inadequate".
        Assert.NotEqual(Reasons.ToWire(RemedyReason.InputMissing),
            Reasons.ToWire(RemedyReason.CatalogueMissing));
    }
}