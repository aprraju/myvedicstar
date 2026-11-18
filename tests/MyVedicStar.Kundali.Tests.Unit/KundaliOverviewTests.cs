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
    private readonly KundaliFacade facade;
    private static readonly CancellationToken Token = CancellationToken.None;

    private static readonly KundaliAstronomyMetadata Metadata =
        new("analytical", "analytical", "Lahiri", "WholeSign", "Mean", "Sidereal");

    public KundaliOverviewTests()
    {
        profiles.GetRegisteredAccountIdAsync(user, Token).Returns(Guid.NewGuid());
        metadata.GetAsync(user, Token).Returns(Metadata);
        facade = new KundaliFacade(profiles, repository, new KundaliValidator(new FixedClock(), KundaliEngineVersions.Foundation),
            KundaliEngineVersions.Foundation, metadata);
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

        // No engine is wired yet, so every astrological block must be null AND explained.
        Assert.Null(overview.Ascendant);
        Assert.Null(overview.MoonSign);
        Assert.Null(overview.SunSign);
        Assert.Null(overview.CurrentDasha);
        Assert.Empty(overview.KeyPlacements);

        var paths = overview.Unsupported.Select(u => u.Path).OrderBy(p => p).ToArray();
        Assert.Equal(["Ascendant", "CurrentDasha", "KeyPlacements", "MoonSign", "SunSign"], paths);

        // The reason must distinguish "not built yet" from "birth time unknown"; conflating them would tell the
        // user we cannot know something that is actually just not implemented.
        Assert.All(overview.Unsupported, u => Assert.Equal(KundaliUnsupportedReasons.EngineUnavailable, u.Reason));
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

