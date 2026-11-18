using System.Security.Claims;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;
using NSubstitute;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// KUN-05 overview slice. The important assertions here are about absence: an unavailable block must be reported in
/// <c>Unsupported</c> with a reason, and a birth time the profile does not carry must be omitted rather than
/// defaulted. Both are cases where a plausible implementation silently invents a value.
/// </summary>
public sealed class KundaliOverviewTests
{
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly Guid profileId = Guid.NewGuid();
    private readonly IKundaliProfileReader profiles = Substitute.For<IKundaliProfileReader>();
    private readonly IKundaliRepository repository = Substitute.For<IKundaliRepository>();
    private readonly IKundaliAstronomyMetadataReader metadata = Substitute.For<IKundaliAstronomyMetadataReader>();
    private readonly IKundaliCalculationEngine engine;
    private readonly KundaliFacade facade;
    private static readonly CancellationToken Token = CancellationToken.None;

    private static readonly KundaliAstronomyMetadata Metadata =
        new("analytical", "analytical", "Lahiri", "WholeSign", "Mean", "Sidereal");

    public KundaliOverviewTests()
    {
        profiles.GetRegisteredAccountIdAsync(user, Token).Returns(Guid.NewGuid());
        // The overview read now binds its payload to a profile revision (KUN-21), so the fixture supplies the snapshot as
        // well as the display. Both describe the same owned profile, exactly as the real reader does.
        profiles.GetSnapshotAsync(user, profileId, Token).Returns(
            new KundaliInputSnapshot(new DateOnly(1990, 4, 12), BirthTimeMode.Known, new TimeOnly(6, 15, 0),
                new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0), 13.0827m, 80.2707m, "Asia/Kolkata", 1));
        metadata.GetAsync(user, Token).Returns(Metadata);
        profiles.GetAyanamsaAsync(user, Token).Returns("Lahiri");
        engine = Substitute.For<IKundaliCalculationEngine>();
        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(null, null, null, null, null, null, null, Array.Empty<KundaliEngineGap>()));
        facade = new KundaliFacade(profiles, repository, new KundaliValidator(new FixedClock(), KundaliEngineVersions.Foundation),
            KundaliEngineVersions.Foundation, metadata, engine, Mapper(), Rules(), new TestAshtakavargaRules(),
            new TestShadbalaRules(), new FixedClock(), Substitute.For<IKundaliResultCache>());
    }

    /// <summary>Decorative Shadbala rules for facade-level tests. The arithmetic is tested exhaustively in
    /// ShadbalaTests against the calculation project; these tests exist to prove the facade's reporting contract - what
    /// it publishes, what it withholds, and why.</summary>
    private sealed class TestShadbalaRules : IKundaliShadbalaRules
    {
        public string RulesVersion => "shadbala-test-1";

        public string Unit => "rupas";

        public IReadOnlyList<string> ScoredPlanets => ["Sun", "Moon", "Mars", "Mercury", "Jupiter", "Venus", "Saturn"];

        public IReadOnlyDictionary<string, string> WithheldComponents =>
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Kala"] = KundaliUnsupportedReasons.RequiresExactBirthTime,
                ["Chesta"] = KundaliUnsupportedReasons.EngineUnavailable
            };

        public IReadOnlyList<KundaliShadbalaResult> Compute(IReadOnlyDictionary<string, double> longitudeByPlanet) =>
            ScoredPlanets.Select(planet => new KundaliShadbalaResult(
                planet,
                [
                    new KundaliShadbalaComponent("Sthana", 0.5, "test basis", "computed"),
                    new KundaliShadbalaComponent("Dig", 0.0, "test placeholder", "placeholder")
                ],
                0.5,
                ["Kala", "Chesta"],
                5.0,
                0.1,
                ["Sthana"],
                ["Dig"])).ToArray();
    }
    }

    /// <summary>Real Ashtakavarga rules over the calculation project.</summary>
    private sealed class TestAshtakavargaRules : IKundaliAshtakavargaRules
{
        public string RulesVersion => MyVedicStar.Kundali.Calculation.Ashtakavarga.RulesVersion;

        public IReadOnlyList<string> ScoredPlanets => MyVedicStar.Kundali.Calculation.Ashtakavarga.ScoredPlanets;

        public IReadOnlyList<int> Bhinna(string planet, IReadOnlyDictionary<string, int> signByReference) =>
            MyVedicStar.Kundali.Calculation.Ashtakavarga.Bhinna(planet, signByReference);

        public IReadOnlyList<int> Sarva(IReadOnlyDictionary<string, int[]> bhinnaByPlanet) =>
            MyVedicStar.Kundali.Calculation.Ashtakavarga.Sarva(bhinnaByPlanet);
}
    /// <summary>Real rules over the calculation project: the dignity tests assert the rules, not a stub.</summary>
    private static IKundaliDignityRules Rules() => new TestDignityRules();

    private sealed class TestDignityRules : IKundaliDignityRules
    {
        public IReadOnlyList<string> DignityStates =>
            MyVedicStar.Kundali.Calculation.PlanetaryDignity.Dignities;

        public string DignityOf(string planet, double siderealLongitude) =>
            MyVedicStar.Kundali.Calculation.PlanetaryDignity.Of(planet, siderealLongitude);

        public IReadOnlyList<int> AspectedSigns(string planet, int fromSign) =>
            MyVedicStar.Kundali.Calculation.PlanetaryDignity.AspectedSigns(planet, fromSign);
    }


    [Fact]
    public async Task Ashtakavarga_is_withheld_while_the_ascendant_is_unvalidated()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(10.0, 45.0, 0, 1, null, null, null,
                Array.Empty<KundaliEngineGap>()));

        var section = await facade.GetAshtakavargaAsync(user, profileId, Token);

        // Every classical table is counted from the ascendant as one of its eight references. Without it the whole
        // table is wrong rather than partial, so nothing is scored.
        Assert.Empty(section.Bhinna);
        Assert.Empty(section.Sarva);
        Assert.Equal(0, section.Totals.GrandTotal);

        var reasons = section.Unsupported.ToDictionary(gap => gap.Path, gap => gap.Reason);
        Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference, reasons["Ascendant"]);

        // Pinned to the ACCURATE code, not merely 'is present'. The birth time here is exact, so labelling the gap
        // requires-exact-birth-time would send a user to correct a birth time that is already correct. The cause is the
        // unvalidated ascendant, and the reason must say so.
        Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference,
            reasons[nameof(KundaliAshtakavargaSection.Bhinna)]);
        Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference,
            reasons[nameof(KundaliAshtakavargaSection.Sarva)]);

        // And it must NOT claim the birth time is the problem.
        Assert.NotEqual(KundaliUnsupportedReasons.RequiresExactBirthTime,
            reasons[nameof(KundaliAshtakavargaSection.Bhinna)]);

        // The rules version is still reported, so a client can see which tables WOULD have been used.
        Assert.False(string.IsNullOrWhiteSpace(section.RulesVersion));
    }

    [Fact]
    public async Task A_foreign_profile_is_not_found_for_ashtakavarga()
    {
        profiles.GetDisplayAsync(user, profileId, Token).Returns((KundaliProfileDisplay?)null);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            facade.GetAshtakavargaAsync(user, profileId, Token));
    }

    /// <summary>Real mapper over the calculation project: the divisional tests assert the rule, not a stub.</summary>
    private static IKundaliDivisionalMapper Mapper() => new TestDivisionalMapper();

    private sealed class TestDivisionalMapper : IKundaliDivisionalMapper
    {
        public bool IsImplemented(int divisional) =>
            MyVedicStar.Kundali.Calculation.DivisionalCharts.IsImplemented(divisional);

        public int SignIn(int divisional, double siderealLongitude) =>
            MyVedicStar.Kundali.Calculation.DivisionalCharts.SignIn(divisional, siderealLongitude);
    }

    private static KundaliProfileDisplay Display(BirthTimeMode mode, TimeOnly? time = null,
        TimeOnly? from = null, TimeOnly? to = null) =>
        new("Asha", new DateOnly(1990, 4, 12), mode, time, from, to, "Chennai", 13.0827m, 80.2707m, "Asia/Kolkata");

    [Fact]
    public async Task Exact_birth_time_is_returned_as_birthTime_with_no_window()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        var overview = await facade.GetOverviewAsync(user, profileId, Token);

        Assert.Equal(KundaliBirthTimeMode.Exact, overview.BirthDetails.BirthTimeMode);
        Assert.Equal(new TimeOnly(6, 45), overview.BirthDetails.BirthTime);
        Assert.Null(overview.BirthDetails.BirthTimeFrom);
        Assert.Null(overview.BirthDetails.BirthTimeTo);
    }

    [Fact]
    public async Task Approximate_birth_time_is_returned_as_a_window_and_never_as_a_single_time()
    {
        profiles.GetDisplayAsync(user, profileId, Token).Returns(
            Display(BirthTimeMode.Approximate, from: new TimeOnly(5, 0), to: new TimeOnly(7, 30)));

        var overview = await facade.GetOverviewAsync(user, profileId, Token);

        Assert.Equal(KundaliBirthTimeMode.Approximate, overview.BirthDetails.BirthTimeMode);
        // A range must never collapse to one instant: that would present an estimate as a fact.
        Assert.Null(overview.BirthDetails.BirthTime);
        Assert.Equal(new TimeOnly(5, 0), overview.BirthDetails.BirthTimeFrom);
        Assert.Equal(new TimeOnly(7, 30), overview.BirthDetails.BirthTimeTo);
    }

    [Fact]
    public async Task Unknown_birth_time_omits_every_time_field_rather_than_using_a_sentinel()
    {
        profiles.GetDisplayAsync(user, profileId, Token).Returns(Display(BirthTimeMode.Unknown));

        var overview = await facade.GetOverviewAsync(user, profileId, Token);

        Assert.Equal(KundaliBirthTimeMode.Unknown, overview.BirthDetails.BirthTimeMode);
        Assert.Null(overview.BirthDetails.BirthTime);
        Assert.Null(overview.BirthDetails.BirthTimeFrom);
        Assert.Null(overview.BirthDetails.BirthTimeTo);
    }

    [Fact]
    public async Task Birth_details_and_conventions_are_populated_from_the_owned_profile_and_settings()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        var overview = await facade.GetOverviewAsync(user, profileId, Token);

        Assert.Equal(profileId, overview.ProfileId);
        Assert.Equal("Asha", overview.BirthDetails.Name);
        Assert.Equal(new DateOnly(1990, 4, 12), overview.BirthDetails.BirthDate);
        Assert.Equal("Chennai", overview.BirthDetails.PlaceName);
        Assert.Equal(13.0827m, overview.BirthDetails.Latitude);
        Assert.Equal("Asia/Kolkata", overview.BirthDetails.TimezoneId);
        // Provenance comes from the configured provider, not a literal in the API layer.
        Assert.Equal("analytical", overview.Conventions.EphemerisProvider);
        Assert.Equal("Lahiri", overview.Conventions.Ayanamsa);
        Assert.Equal("Sidereal", overview.Conventions.Zodiac);
    }

    [Fact]
    public async Task Unavailable_blocks_are_reported_with_engine_unavailable_not_as_birth_time_uncertainty()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        var overview = await facade.GetOverviewAsync(user, profileId, Token);

        // Ascendant is computed-capable but unproven, placements are not built yet. Both are absent, but the reasons
        // must differ: "not yet proven" is a different claim from "not implemented".
        Assert.Null(overview.Ascendant);
        Assert.Empty(overview.KeyPlacements);

        var reasons = overview.Unsupported.ToDictionary(u => u.Path, u => u.Reason);
        Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference, reasons["Ascendant"]);
        Assert.Equal(KundaliUnsupportedReasons.EngineUnavailable, reasons["KeyPlacements"]);
        // Dasha is built now, so it must not be reported unavailable. This stub engine returns no sequence, which is
        // itself a gap it already declared - the facade must not invent a second reason for the same absence.
        Assert.DoesNotContain("CurrentDasha", reasons.Keys);
        // Sun and moon are produced by the engine now, so they must NOT appear as unsupported.
        Assert.DoesNotContain("MoonSign", reasons.Keys);
        Assert.DoesNotContain("SunSign", reasons.Keys);
    }


    [Fact]
    public async Task An_exact_birth_time_returns_real_moon_and_sun_signs_from_the_engine()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));
        // Sign indices from the engine: 2 = Gemini, 9 = Capricorn. Names are asserted so a mistake in the
        // index-to-name mapping cannot pass as a correct sign.
        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(280.0, 70.0, 9, 2, null, null, null, Array.Empty<KundaliEngineGap>()));

        var overview = await facade.GetOverviewAsync(user, profileId, Token);

        Assert.Equal("Gemini", overview.MoonSign);
        Assert.Equal("Capricorn", overview.SunSign);
        Assert.DoesNotContain(overview.Unsupported, u => u.Path is "MoonSign" or "SunSign");
    }

    [Theory]
    [InlineData(BirthTimeMode.Approximate)]
    [InlineData(BirthTimeMode.Unknown)]
    public async Task A_non_exact_birth_time_suppresses_the_signs_with_the_exact_time_reason(BirthTimeMode mode)
    {
        profiles.GetDisplayAsync(user, profileId, Token).Returns(
            mode == BirthTimeMode.Approximate
                ? Display(BirthTimeMode.Approximate, from: new TimeOnly(5, 0), to: new TimeOnly(7, 30))
                : Display(BirthTimeMode.Unknown));
        // The engine reports the suppression; the facade must surface its reason verbatim rather than re-labelling
        // it, or "we cannot know your birth time" would be flattened into "not implemented".
        engine.Calculate(Arg.Any<KundaliEngineInput>()).Returns(new KundaliEngineResult(
            null, null, null, null, null, null, null,
            [
                new KundaliEngineGap("SunSign", KundaliEngineReasons.RequiresExactBirthTime),
                new KundaliEngineGap("MoonSign", KundaliEngineReasons.RequiresExactBirthTime)
            ]));

        var overview = await facade.GetOverviewAsync(user, profileId, Token);

        Assert.Null(overview.SunSign);
        Assert.Null(overview.MoonSign);
        var reasons = overview.Unsupported.ToDictionary(u => u.Path, u => u.Reason);
        Assert.Equal(KundaliUnsupportedReasons.RequiresExactBirthTime, reasons["SunSign"]);
        Assert.Equal(KundaliUnsupportedReasons.RequiresExactBirthTime, reasons["MoonSign"]);
        // Ascendant keeps its own distinct reason even when the time is also inexact - it is withheld for a
        // different cause and the two must not be conflated.
        Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference, reasons["Ascendant"]);
    }

    [Fact]
    public async Task A_foreign_or_missing_profile_is_not_found_and_never_leaks_existence()
    {
        profiles.GetDisplayAsync(user, profileId, Token).Returns((KundaliProfileDisplay?)null);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => facade.GetOverviewAsync(user, profileId, Token));
        // Ownership is decided before any provider read, so a foreign id cannot even trigger metadata work.
        await metadata.DidNotReceive().GetAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_profile_id_is_not_found_without_touching_the_profile_reader()
    {
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => facade.GetOverviewAsync(user, Guid.Empty, Token));
        await profiles.DidNotReceive().GetDisplayAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_dasha_containing_now_is_reported_with_its_absolute_utc_bounds()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        // FixedClock is 2026-11-17T12:00Z, so the second period is the one holding "now".
        var firstStart = new DateTimeOffset(1989, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var firstEnd = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var secondEnd = new DateTimeOffset(2060, 1, 1, 0, 0, 0, TimeSpan.Zero);
        ReturnDashaSequence(new KundaliEngineDasha("Venus", firstStart, firstEnd, true),
            new KundaliEngineDasha("Sun", firstEnd, secondEnd, false));

        var overview = await facade.GetOverviewAsync(user, profileId, Token);

        Assert.NotNull(overview.CurrentDasha);
        Assert.Equal("Sun", overview.CurrentDasha.Lord);
        Assert.Equal("Mahadasha", overview.CurrentDasha.Level);
        // Bounds are absolute instants, and are passed through untouched: converting to the profile's zone here
        // would bake a rendering decision into the data.
        Assert.Equal(firstEnd, overview.CurrentDasha.StartUtc);
        Assert.Equal(secondEnd, overview.CurrentDasha.EndUtc);
        Assert.DoesNotContain("CurrentDasha", overview.Unsupported.Select(u => u.Path));
    }

    [Fact]
    public async Task A_period_boundary_belongs_to_the_later_period_and_never_to_both()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        // The boundary is exactly "now": half-open intervals mean the later period claims it.
        var boundary = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        ReturnDashaSequence(
            new KundaliEngineDasha("Venus", new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero), boundary, true),
            new KundaliEngineDasha("Sun", boundary, new DateTimeOffset(2050, 1, 1, 0, 0, 0, TimeSpan.Zero), false));

        var overview = await facade.GetOverviewAsync(user, profileId, Token);

        Assert.NotNull(overview.CurrentDasha);
        Assert.Equal("Sun", overview.CurrentDasha.Lord);
    }

    [Fact]
    public async Task A_moment_outside_the_built_sequence_is_reported_rather_than_clamped_to_a_nearby_dasha()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        // Both periods are in the past relative to FixedClock. Clamping would state a dasha the person is not in.
        ReturnDashaSequence(
            new KundaliEngineDasha("Venus", new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero), true),
            new KundaliEngineDasha("Sun", new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), false));

        var overview = await facade.GetOverviewAsync(user, profileId, Token);

        Assert.Null(overview.CurrentDasha);
        var reasons = overview.Unsupported.ToDictionary(u => u.Path, u => u.Reason);
        Assert.True(reasons.ContainsKey("CurrentDasha"));
    }

    private void ReturnDashaSequence(params KundaliEngineDasha[] periods) =>
        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(null, null, null, null, periods, null, null, Array.Empty<KundaliEngineGap>()));

    [Fact]
    public async Task An_unknown_dasha_system_is_rejected_rather_than_silently_served_as_vimshottari()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        // Yogini is served now, so this uses a system that genuinely does not exist. Returning Vimshottari dates for
        // an Ashtottari request would be a fabricated answer, and refusing is the only honest response.
        await Assert.ThrowsAsync<ValidationException>(() =>
            facade.GetDashasAsync(user, profileId, "ashtottari", null, Token));

        // The rejection happens before any provider or engine work.
        await metadata.DidNotReceive().GetAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
        engine.DidNotReceive().CalculateDashaTimeline(Arg.Any<KundaliDashaQuery>());
    }

    [Fact]
    public async Task The_resolved_query_instant_is_echoed_so_the_caller_never_assumes_server_now()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        engine.CalculateDashaTimeline(Arg.Any<KundaliDashaQuery>())
            .Returns(new KundaliEngineDashaTimeline(87.3, [], [], null, null, Array.Empty<KundaliEngineGap>()));

        var timeline = await facade.GetDashasAsync(user, profileId, "vimshottari", null, Token);

        // Omitted "at" resolves to the clock, and the same instant comes back in the payload.
        Assert.Equal(new FixedClock().GetUtcNow(), timeline.At);
        Assert.Equal("vimshottari", timeline.System);
        Assert.Equal("Asia/Kolkata", timeline.DisplayTimezone);
    }

    [Fact]
    public async Task An_explicit_instant_is_honoured_instead_of_being_replaced_by_the_clock()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        var past = new DateTimeOffset(2015, 3, 1, 0, 0, 0, TimeSpan.Zero);
        engine.CalculateDashaTimeline(Arg.Any<KundaliDashaQuery>())
            .Returns(new KundaliEngineDashaTimeline(87.3, [], [], null, null, Array.Empty<KundaliEngineGap>()));

        var timeline = await facade.GetDashasAsync(user, profileId, "vimshottari", past, Token);

        Assert.Equal(past, timeline.At);
        engine.Received(1).CalculateDashaTimeline(Arg.Is<KundaliDashaQuery>(query => query.At == past));
    }

    [Fact]
    public async Task A_foreign_profile_is_not_found_for_dashas_too()
    {
        profiles.GetDisplayAsync(user, profileId, Token).Returns((KundaliProfileDisplay?)null);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            facade.GetDashasAsync(user, profileId, "vimshottari", null, Token));
    }

    [Fact]
    public async Task A_known_but_unavailable_system_is_answered_with_a_reason_not_a_rejection()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        engine.CalculateDashaTimeline(Arg.Any<KundaliDashaQuery>())
            .Returns(new KundaliEngineDashaTimeline(87.3, [], [], null, null, Array.Empty<KundaliEngineGap>()));

        // "chara" exists as a concept, so it is not an unknown-system error. It is withheld because its period
        // lengths need an ascendant we have not validated.
        var timeline = await facade.GetDashasAsync(user, profileId, "chara", null, Token);

        Assert.Equal("chara", timeline.System);
        Assert.Empty(timeline.Mahadashas);
        var reasons = timeline.Unsupported.ToDictionary(u => u.Path, u => u.Reason);
        Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference, reasons["Mahadashas"]);
    }

    [Fact]
    public async Task A_genuinely_unknown_system_is_a_validation_error()
    {
        // Distinct from "chara": a system that does not exist is caller error, not a capability gap.
        await Assert.ThrowsAsync<ValidationException>(() =>
            facade.GetDashasAsync(user, profileId, "ashtottari", null, Token));
    }

    [Fact]
    public async Task Yogini_periods_are_served_with_the_current_one_selected()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        // Spans FixedClock's 2026-11-17 so the period is genuinely current; an earlier fixture ended in 2025 and
        // therefore tested the not-current branch while asserting the current one.
        var firstStart = new DateTimeOffset(1989, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var firstEnd = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        engine.CalculateDashaTimeline(Arg.Any<KundaliDashaQuery>()).Returns(new KundaliEngineDashaTimeline(
            87.3,
            [],
            [],
            [
                new KundaliEngineYoginiPeriod("Mangala", "Moon", firstStart, firstEnd, true),
            ],
            null,
            Array.Empty<KundaliEngineGap>()));

        var timeline = await facade.GetDashasAsync(user, profileId, "yogini", null, Token);

        Assert.Equal("yogini", timeline.System);
        Assert.Single(timeline.Mahadashas);
        Assert.Equal("Mangala", timeline.Mahadashas[0].Lord);
        // The current path is the single containing period; Yogini sub-periods are not built, and that is reported.
        Assert.Single(timeline.PeriodPath);
        Assert.Contains(timeline.Unsupported, u => u.Path == "PeriodPath");
    }

    [Fact]
    public async Task A_chart_with_an_exact_birth_time_places_sun_and_moon_and_says_so()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        // Sun at 10 Aries, moon at 15 Taurus.
        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(10.0, 45.0, 0, 1, null, null, null,
                Array.Empty<KundaliEngineGap>()));

        var section = await facade.GetChartsAsync(user, profileId, 9, Token);

        Assert.Equal(9, section.Divisional);
        Assert.Equal("Navamsa", section.Method);
        Assert.Equal("validated", section.Precision.Status);
        Assert.Null(section.Precision.Reason);

        // Every sign is present even when empty: a chart missing houses is not a chart.
        Assert.Equal(12, section.Houses.Count);

        var placed = section.Houses.SelectMany(house => house.Placements).ToArray();
        Assert.Equal(2, placed.Length);
        Assert.Contains(placed, placement => placement.Planet == "Sun");
        Assert.Contains(placed, placement => placement.Planet == "Moon");
    }

    [Fact]
    public async Task An_inexact_birth_time_yields_an_empty_chart_with_a_reason_not_a_guessed_one()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Approximate, from: new TimeOnly(5, 0), to: new TimeOnly(7, 30)));

        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(null, null, null, null, null, null, null,
                [new KundaliEngineGap("SunSign", KundaliEngineReasons.RequiresExactBirthTime)]));

        var section = await facade.GetChartsAsync(user, profileId, 9, Token);

        // No houses rather than twelve empty ones: an all-empty chart reads as "computed, nothing placed", which is a
        // different and false claim from "cannot be computed".
        Assert.Empty(section.Houses);
        Assert.Equal("unvalidated-reference", section.Precision.Status);
        Assert.Equal(KundaliUnsupportedReasons.RequiresExactBirthTime, section.Precision.Reason);
    }

    [Fact]
    public async Task An_unknown_division_is_rejected_before_any_provider_work()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            facade.GetChartsAsync(user, profileId, 5, Token));

        await metadata.DidNotReceive().GetAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_foreign_profile_is_not_found_for_charts()
    {
        profiles.GetDisplayAsync(user, profileId, Token).Returns((KundaliProfileDisplay?)null);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            facade.GetChartsAsync(user, profileId, 1, Token));
    }

    [Fact]
    public async Task Rules_are_reported_for_all_nine_grahas_even_though_only_two_have_positions()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(10.0, 45.0, 0, 1, null, null, null,
                Array.Empty<KundaliEngineGap>()));

        var section = await facade.GetPlanetsAsync(user, profileId, Token);

        // The rule capability is chart-independent: all nine bodies are covered by dignity/aspect rules.
        Assert.Equal(9, section.Rules.BodiesWithPositions.Count + section.Rules.BodiesWithoutPositions.Count);
        Assert.Equal(2, section.Rules.BodiesWithPositions.Count);

        // Placements exist only for the two the engine could place.
        Assert.Equal(2, section.Planets.Count);
        Assert.All(section.Planets, planet => Assert.Contains(planet.Planet, section.Rules.BodiesWithPositions));
    }

    [Fact]
    public async Task Every_graha_without_a_position_is_named_rather_than_silently_omitted()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(10.0, 45.0, 0, 1, null, null, null,
                Array.Empty<KundaliEngineGap>()));

        var section = await facade.GetPlanetsAsync(user, profileId, Token);

        // A client must be able to tell "no Mars in this chart" from "no Mars position available at all".
        foreach (var body in new[] { "Mars", "Mercury", "Jupiter", "Venus", "Saturn", "Rahu", "Ketu" })
            Assert.Contains(section.Unsupported, gap => gap.Path == $"Planets.{body}");
    }

    [Fact]
    public async Task Dignity_is_computed_from_the_rule_not_defaulted()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        // Sun at 10 Aries is exalted. Moon at 105 (Cancer 15) is not - the first attempt used 45, which is Taurus,
        // the Moon's OWN exaltation sign, so the fixture accidentally asserted the opposite of what it read like.
        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(10.0, 105.0, 0, 3, null, null, null,
                Array.Empty<KundaliEngineGap>()));

        var section = await facade.GetPlanetsAsync(user, profileId, Token);

        Assert.Equal("Exalted", section.Planets.Single(p => p.Planet == "Sun").Dignity);
        Assert.NotEqual("Exalted", section.Planets.Single(p => p.Planet == "Moon").Dignity);
        // Asserted positively too, so the test cannot pass merely because Exalted became unreachable.
        Assert.Equal("Own", section.Planets.Single(p => p.Planet == "Moon").Dignity);
    }

    [Fact]
    public async Task Unknown_retrograde_and_house_are_null_not_false()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(10.0, 45.0, 0, 1, null, null, null,
                Array.Empty<KundaliEngineGap>()));

        var section = await facade.GetPlanetsAsync(user, profileId, Token);

        // Reporting false would be a factual claim the engine cannot support.
        var sun = section.Planets.Single(p => p.Planet == "Sun");
        Assert.Null(sun.Retrograde);
        Assert.Null(sun.Combust);
        Assert.Null(sun.House);
    }

    [Fact]
    public async Task An_inexact_birth_time_yields_no_placements_and_says_why()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Approximate, from: new TimeOnly(5, 0), to: new TimeOnly(7, 30)));

        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(null, null, null, null, null, null, null,
                [new KundaliEngineGap("SunSign", KundaliEngineReasons.RequiresExactBirthTime)]));

        var section = await facade.GetPlanetsAsync(user, profileId, Token);

        Assert.Empty(section.Planets);
        // The rules are still reported, because they do not depend on the birth time.
        Assert.NotEmpty(section.Rules.DignityStates);
        Assert.Contains(section.Unsupported, gap => gap.Path == "Planets.Sun");
    }

    [Fact]
    public async Task A_foreign_profile_is_not_found_for_planets()
    {
        profiles.GetDisplayAsync(user, profileId, Token).Returns((KundaliProfileDisplay?)null);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            facade.GetPlanetsAsync(user, profileId, Token));
    }


    [Fact]
    public async Task Bala_reports_that_it_is_incomplete_and_names_both_missing_components()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(10.0, 45.0, 0, 1, null, null, null,
                Array.Empty<KundaliEngineGap>())
            {
                GrahaPositions = FullGrahaPositions()
            });

        var section = await facade.GetBalaAsync(user, profileId, Token);

        // The central claim of this section: it must not be possible to read the total as a six-fold score.
        Assert.False(section.Complete);

        var reasons = section.Unsupported.ToDictionary(gap => gap.Path, gap => gap.Reason, StringComparer.Ordinal);
        Assert.Equal(KundaliUnsupportedReasons.RequiresExactBirthTime, reasons["Kala"]);
        Assert.Equal(KundaliUnsupportedReasons.EngineUnavailable, reasons["Chesta"]);

        foreach (var planet in section.Shadbala)
        {
            Assert.Contains("Kala", planet.ComponentsWithheld);
            Assert.Contains("Chesta", planet.ComponentsWithheld);
        }

        Assert.NotEmpty(section.Shadbala);
        Assert.Equal("shadbala-test-1", section.RulesVersion);
        Assert.Equal("rupas", section.Unit);
    }

    [Fact]
    public async Task Bhava_bala_is_withheld_because_the_ascendant_is_unvalidated()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(10.0, 45.0, 0, 1, null, null, null,
                Array.Empty<KundaliEngineGap>())
            {
                GrahaPositions = FullGrahaPositions()
            });

        var section = await facade.GetBalaAsync(user, profileId, Token);

        // Twelve zeros would read as "computed and weak", which is a different and false statement from "not computed".
        Assert.Empty(section.BhavaBala);

        var reasons = section.Unsupported.ToDictionary(gap => gap.Path, gap => gap.Reason, StringComparer.Ordinal);
        Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference, reasons[nameof(KundaliBalaSection.BhavaBala)]);

        // And it must NOT blame the user's birth time, which is exact in this test.
        Assert.NotEqual(KundaliUnsupportedReasons.RequiresExactBirthTime,
            reasons[nameof(KundaliBalaSection.BhavaBala)]);
    }

    [Fact]
    public async Task Bala_withholds_shadbala_when_a_scored_planet_has_no_position()
    {
        profiles.GetDisplayAsync(user, profileId, Token)
            .Returns(Display(BirthTimeMode.Known, new TimeOnly(6, 45)));

        // Saturn withheld: six planets present. Ranking six planets while the seventh is simply unlooked-at would put
        // Saturn at the bottom for a reason that has nothing to do with the chart.
        var partial = FullGrahaPositions()
            .Where(graha => !string.Equals(graha.Graha, "Saturn", StringComparison.Ordinal))
            .ToArray();

        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(10.0, 45.0, 0, 1, null, null, null,
                Array.Empty<KundaliEngineGap>())
            {
                GrahaPositions = partial
            });

        var section = await facade.GetBalaAsync(user, profileId, Token);

        Assert.Empty(section.Shadbala);
        Assert.False(section.Complete);

        var reasons = section.Unsupported.ToDictionary(gap => gap.Path, gap => gap.Reason, StringComparer.Ordinal);
        Assert.Equal(KundaliUnsupportedReasons.EngineUnavailable, reasons["Saturn"]);
        Assert.Equal(KundaliUnsupportedReasons.EngineUnavailable, reasons[nameof(KundaliBalaSection.Shadbala)]);
    }

    private static IReadOnlyList<KundaliEngineGrahaPosition> FullGrahaPositions() =>
        new[]
        {
            new KundaliEngineGrahaPosition("Sun", 10.0, "Aries", 10.0m, false, 0.98, null),
            new KundaliEngineGrahaPosition("Moon", 100.0, "Cancer", 10.0m, false, 13.2, null),
            new KundaliEngineGrahaPosition("Mars", 200.0, "Libra", 20.0m, false, 0.52, null),
            new KundaliEngineGrahaPosition("Mercury", 250.0, "Sagittarius", 10.0m, true, 1.2, null),
            new KundaliEngineGrahaPosition("Jupiter", 300.0, "Capricorn", 0.0m, false, 0.08, null),
            new KundaliEngineGrahaPosition("Venus", 60.0, "Gemini", 0.0m, false, 1.2, null),
            new KundaliEngineGrahaPosition("Saturn", 330.0, "Pisces", 0.0m, true, 0.03, null)
        };

}
