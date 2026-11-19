using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.VedicProfiles.Shared;
using NSubstitute;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// #113: every unsupported field must name its real cause, and never blame a birth time that may already be correct.
/// </summary>
/// <remarks>
/// <para>Each assertion pins the exact reason code for a specific path. A presence check ("the field is reported")
/// passes for any code, including a plausible wrong one - which is exactly how these four survived until now. So every
/// test here asserts <see cref="KundaliUnsupportedReasons.UnvalidatedReference"/> or
/// <see cref="KundaliUnsupportedReasons.OutsideDashaSpan"/> by identity.</para>
/// <para>The fixtures deliberately have an EXACT birth time. That is the condition under which the old codes were
/// demonstrably wrong: with a known birth time there is nothing for the reader to go and correct, so a
/// <c>requires-exact-birth-time</c> reason was telling them to fix input that was already right.</para>
/// </remarks>
public sealed class KundaliUnsupportedReasonTests
{
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly Guid profileId = Guid.NewGuid();
    private readonly IKundaliProfileReader profiles = Substitute.For<IKundaliProfileReader>();
    private readonly IKundaliRepository repository = Substitute.For<IKundaliRepository>();
    private readonly IKundaliAlertRepository alerts = Substitute.For<IKundaliAlertRepository>();
    private readonly IKundaliAstronomyMetadataReader metadata = Substitute.For<IKundaliAstronomyMetadataReader>();
    private readonly IKundaliCalculationEngine engine;
    private readonly IKundaliFacade facade;
    private static readonly CancellationToken Token = CancellationToken.None;

    public KundaliUnsupportedReasonTests()
    {
        profiles.GetRegisteredAccountIdAsync(user, Token).Returns(Guid.NewGuid());
        profiles.GetSnapshotAsync(user, profileId, Token).Returns(
            new KundaliInputSnapshot(new DateOnly(1990, 4, 12), BirthTimeMode.Known, new TimeOnly(6, 15, 0),
                new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0), 13.0827m, 80.2707m, "Asia/Kolkata", 1));
        profiles.GetDisplayAsync(user, profileId, Token).Returns(new KundaliProfileDisplay("Test",
            new DateOnly(1990, 4, 12), BirthTimeMode.Known, new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0),
            new TimeOnly(6, 15, 0), "Chennai", 13.0827m, 80.2707m, "Asia/Kolkata"));
        profiles.GetAyanamsaAsync(user, Token).Returns("Lahiri");
        metadata.GetAsync(user, Token).Returns(new KundaliAstronomyMetadata("analytical", "analytical", "Lahiri",
            "WholeSign", "Mean", "Sidereal"));

        // The engine produces nothing: that is the state in which these paths report their reason.
        engine = Substitute.For<IKundaliCalculationEngine>();
        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(null, null, null, null, null, null, null, Array.Empty<KundaliEngineGap>()));

        // api#124: the container resolves the facade, so a new constructor parameter is one registration in
        // KundaliTestProvider rather than an edit here.
        facade = KundaliTestProvider.Create()
            .With(profiles)
            .With(repository)
            .With(alerts)
            .With(metadata)
            .With(engine)
            .With(Substitute.For<IKundaliResultCache>())
            .With<IKundaliDivisionalMapper>(new PassthroughDivisions())
            .With<IKundaliDignityRules>(new NoDignityRules3())
            .With<IKundaliAshtakavargaRules>(new NoAshtakavargaRules3())
            .With<IKundaliShadbalaRules>(new NoShadbalaRules3())
            .With<IKundaliYogaRules>(new NoYogaRules3())
            .BuildFacade();
    }

    [Fact]
    public async Task Chart_precision_status_and_reason_agree_and_blame_the_reference_not_the_birth_time()
    {
        var section = await facade.GetChartsAsync(user, profileId, 1, Token);

        // Before #113 this object said status "unvalidated-reference" and reason "requires-exact-birth-time" in the
        // same construction: two fields describing one fact, disagreeing about it.
        Assert.Equal("unvalidated-reference", section.Precision.Status);
        Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference, section.Precision.Reason);
        Assert.NotEqual(KundaliUnsupportedReasons.RequiresExactBirthTime, section.Precision.Reason);
    }

    [Fact]
    public async Task Every_chart_precision_reason_matches_its_status()
    {
        // The invariant, not just this instance: whatever the status is, the reason must be the cause that status names.
        var section = await facade.GetChartsAsync(user, profileId, 9, Token);

        if (section.Precision.Status == "unvalidated-reference")
            Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference, section.Precision.Reason);
        else
            Assert.Null(section.Precision.Reason);
    }

    [Fact]
    public async Task Retrograde_is_reported_as_an_unvalidated_reference_not_a_birth_time_problem()
    {
        var section = await facade.GetPlanetsAsync(user, profileId, Token);

        var retrograde = Assert.Single(section.Unsupported.Where(field => field.Path == "Planets.Retrograde"));
        Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference, retrograde.Reason);
        Assert.NotEqual(KundaliUnsupportedReasons.RequiresExactBirthTime, retrograde.Reason);
    }

    [Fact]
    public async Task Combustion_is_reported_as_an_unvalidated_reference_not_a_birth_time_problem()
    {
        var section = await facade.GetPlanetsAsync(user, profileId, Token);

        var combust = Assert.Single(section.Unsupported.Where(field => field.Path == "Planets.Combust"));
        Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference, combust.Reason);
        Assert.NotEqual(KundaliUnsupportedReasons.RequiresExactBirthTime, combust.Reason);
    }

    [Fact]
    public async Task A_withheld_sun_or_moon_placement_does_not_blame_the_birth_time()
    {
        var section = await facade.GetPlanetsAsync(user, profileId, Token);

        var sun = Assert.Single(section.Unsupported.Where(field => field.Path == "Planets.Sun"));
        var moon = Assert.Single(section.Unsupported.Where(field => field.Path == "Planets.Moon"));
        Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference, sun.Reason);
        Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference, moon.Reason);
    }

    [Fact]
    public async Task An_instant_outside_the_dasha_span_is_reported_as_such_rather_than_a_birth_time_fault()
    {
        // The sequence exists but does not cover "now": not birth-time uncertainty, and not an unvalidated value.
        engine.Calculate(Arg.Any<KundaliEngineInput>()).Returns(new KundaliEngineResult(
            null, null, null, null,
            [new KundaliEngineDasha("Ketu", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(7), false)],
            null, null, []));

        var overview = await facade.GetOverviewAsync(user, profileId, Token);

        var current = Assert.Single(overview.Unsupported.Where(field => field.Path == "CurrentDasha"));
        Assert.Equal(KundaliUnsupportedReasons.OutsideDashaSpan, current.Reason);
        Assert.NotEqual(KundaliUnsupportedReasons.RequiresExactBirthTime, current.Reason);
    }

    [Fact]
    public void The_facade_never_emits_requires_exact_birth_time_anywhere()
    {
        // The strongest form of the acceptance criterion: no source path can emit it, so no future edit can reintroduce
        // the mislabelling without this test noticing.
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MyVedicStar.Kundali.Business",
            "KundaliFacade.cs"));

        Assert.DoesNotContain("RequiresExactBirthTime", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private sealed class PassthroughDivisions : IKundaliDivisionalMapper
    {
        public bool IsImplemented(int divisional) => true;
        public int SignIn(int divisional, double siderealLongitude) => (int)Math.Floor(siderealLongitude / 30.0) % 12;
    }

    private sealed class NoDignityRules3 : IKundaliDignityRules
    {
        public IReadOnlyList<string> DignityStates => [];
        public string DignityOf(string planet, double siderealLongitude) => "unknown";
        public IReadOnlyList<int> AspectedSigns(string planet, int fromSign) => [];
    }

    private sealed class NoAshtakavargaRules3 : IKundaliAshtakavargaRules
    {
        public string RulesVersion => "none";
        public IReadOnlyList<string> ScoredPlanets => [];
        public IReadOnlyList<int> Bhinna(string planet, IReadOnlyDictionary<string, int> signByReference) => [];
        public IReadOnlyList<int> Sarva(IReadOnlyDictionary<string, int[]> bhinnaByPlanet) => [];
    }

    private sealed class NoYogaRules3 : IKundaliYogaRules
    {
        public string RulesVersion => "none";
        public IReadOnlyList<string> Statuses => [];
        public IReadOnlyList<string> Catalogue => [];
        public KundaliYogaEvaluation Evaluate(IReadOnlyDictionary<string, int> signByGraha) => new("none", [], []);
    }

    private sealed class NoShadbalaRules3 : IKundaliShadbalaRules
    {
        public string RulesVersion => "none";
        public string Unit => "rupas";
        public IReadOnlyList<string> ScoredPlanets => [];
        public IReadOnlyDictionary<string, string> WithheldComponents =>
            new Dictionary<string, string>(StringComparer.Ordinal);
        public IReadOnlyList<KundaliShadbalaResult> Compute(IReadOnlyDictionary<string, double> longitudeByPlanet) => [];
    }
}
