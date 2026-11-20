using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.VedicProfiles.Shared;
using NSubstitute;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// api#154. A finding the engine could NOT assess, whose code the catalogue does not carry, was never visited either:
/// the projection iterates the catalogue, so such a finding appeared in neither <c>items</c> nor <c>unsupported</c>.
/// The client saw an empty list and could not tell "not assessed" from "nothing applies".
/// </summary>
public sealed class RemedyNotEvaluatedInvisibleTests
{
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly Guid accountId = Guid.NewGuid();
    private readonly Guid profileId = Guid.NewGuid();
    private readonly IKundaliProfileReader profiles = Substitute.For<IKundaliProfileReader>();
    private readonly IKundaliRepository repository = Substitute.For<IKundaliRepository>();
    private readonly IKundaliAstronomyMetadataReader metadata = Substitute.For<IKundaliAstronomyMetadataReader>();
    private readonly IKundaliCalculationEngine engine = Substitute.For<IKundaliCalculationEngine>();
    private readonly IKundaliYogaRules yoga = Substitute.For<IKundaliYogaRules>();
    private readonly IKundaliFacade facade;
    private static readonly CancellationToken Token = CancellationToken.None;

    private const string UncataloguedCode = "yoga:not-in-catalogue";
    /// <summary>"unclassified" IS a real catalogue member, so it exercises the opposite branch.</summary>
    private const string CataloguedCode = "unclassified";

    public RemedyNotEvaluatedInvisibleTests()
    {
        profiles.GetRegisteredAccountIdAsync(user, Token).Returns(accountId);
        profiles.GetAyanamsaAsync(user, Token).Returns("Lahiri");
        profiles.GetDisplayAsync(user, profileId, Token).Returns(new KundaliProfileDisplay(
            "Test Person", new DateOnly(1990, 3, 15), BirthTimeMode.Known,
            new TimeOnly(6, 45), null, null, "London", 51.5m, -0.12m, "Europe/London"));
        metadata.GetAsync(user, Token).Returns(new KundaliAstronomyMetadata(
            "analytical", "analytical", "Lahiri", "WholeSign", "Mean", "Sidereal"));

        engine.Calculate(Arg.Any<KundaliEngineInput>()).Returns(new KundaliEngineResult(
            null, null, null, null, null, null, null, Array.Empty<KundaliEngineGap>()));

        facade = KundaliTestProvider.Create()
            .With(profiles)
            .With(repository)
            .With(metadata)
            .With<IKundaliCalculationEngine>(engine)
            .With<IKundaliYogaRules>(yoga)
            .With(Substitute.For<IKundaliResultCache>())
            .BuildFacade();
    }

    private void YogaReports(IReadOnlyList<KundaliYogaResult> evaluated, IReadOnlyList<KundaliYogaResult> notEvaluated)
    {
        yoga.RulesVersion.Returns("test-rules-1");
        yoga.Evaluate(Arg.Any<IReadOnlyDictionary<string, int>>()).Returns(
            new KundaliYogaEvaluation("test-rules-1", evaluated, notEvaluated));
    }

    private static KundaliYogaResult Finding(string code) =>
        new(code, code, "chart", "present", [], [], [], [], [], true, null, []);

    // ---- the case itself (api#154) ----

    [Fact]
    public async Task A_not_evaluated_finding_with_no_catalogue_entry_is_reported_as_input_missing()
    {
        // Before the fix this appeared in NEITHER list: the projection iterates the catalogue, so an uncatalogued
        // finding was never visited. Presence alone is not enough to assert - a row reported for the WRONG reason would
        // pass a presence check while reproducing the exact confusion this issue exists to end, so the reason string is
        // asserted below.
        YogaReports([], [Finding(UncataloguedCode)]);

        var section = await facade.GetRemediesAsync(user, profileId, Token);

        var reported = Assert.Single(section.Unsupported, field => field.Path == UncataloguedCode);
        Assert.Equal("input-missing", reported.Reason);
        Assert.NotEqual("catalogue-missing", reported.Reason);
        Assert.DoesNotContain(section.Items, item => item.FindingCode == UncataloguedCode);
    }

    // ---- regression guards: must pass BOTH before and after ----

    [Fact]
    public async Task A_not_evaluated_finding_with_a_catalogue_entry_still_reports_input_missing()
    {
        // The path that already worked. If this changes, the new pass altered behaviour it was not meant to touch.
        YogaReports([], [Finding(CataloguedCode)]);

        var section = await facade.GetRemediesAsync(user, profileId, Token);

        var reported = Assert.Single(section.Unsupported, field => field.Path == CataloguedCode);
        Assert.Equal("input-missing", reported.Reason);
        Assert.DoesNotContain(section.Items, item => item.FindingCode == CataloguedCode);
    }

    [Fact]
    public async Task An_evaluated_finding_with_no_catalogue_entry_still_reports_catalogue_missing()
    {
        // api#56's property, which must survive this change. The two reasons must not collapse into one.
        YogaReports([Finding(UncataloguedCode)], []);

        var section = await facade.GetRemediesAsync(user, profileId, Token);

        var reported = Assert.Single(section.Unsupported, field => field.Path == UncataloguedCode);
        Assert.Equal("catalogue-missing", reported.Reason);
        Assert.NotEqual("input-missing", reported.Reason);
    }
}