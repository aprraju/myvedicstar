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
        metadata.GetAsync(user, Token).Returns(Metadata);
        engine = Substitute.For<IKundaliCalculationEngine>();
        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(null, null, null, null, Array.Empty<KundaliEngineGap>()));
        facade = new KundaliFacade(profiles, repository, new KundaliValidator(new FixedClock(), KundaliEngineVersions.Foundation),
            KundaliEngineVersions.Foundation, metadata, engine);
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

        // Ascendant is computed-capable but unproven, dasha and placements are not built yet. Both are absent, but
        // the reasons must differ: "not yet proven" is a different claim from "not implemented".
        Assert.Null(overview.Ascendant);
        Assert.Null(overview.CurrentDasha);
        Assert.Empty(overview.KeyPlacements);

        var reasons = overview.Unsupported.ToDictionary(u => u.Path, u => u.Reason);
        Assert.Equal(KundaliUnsupportedReasons.UnvalidatedReference, reasons["Ascendant"]);
        Assert.Equal(KundaliUnsupportedReasons.EngineUnavailable, reasons["CurrentDasha"]);
        Assert.Equal(KundaliUnsupportedReasons.EngineUnavailable, reasons["KeyPlacements"]);
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
            .Returns(new KundaliEngineResult(280.0, 70.0, 9, 2, Array.Empty<KundaliEngineGap>()));

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
            null, null, null, null,
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
}

