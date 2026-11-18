using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Business;
using MyVedicStar.VedicProfiles.Shared;
using NSubstitute;

namespace MyVedicStar.VedicProfiles.UnitTests;

/// <summary>
/// KUN-06 birth-time representation. The window rule is the load-bearing part: an approximate window must run
/// strictly forward on the clock, so a pair that wraps past midnight is rejected rather than clamped or silently
/// reinterpreted as a long window. Owner decision #16.
/// </summary>
public sealed class BirthTimeWindowTests : IDisposable
{
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly Guid profileId = Guid.NewGuid();
    private readonly IVedicProfileAccountReader accounts = Substitute.For<IVedicProfileAccountReader>();
    private readonly IVedicProfileRepository repository = Substitute.For<IVedicProfileRepository>();
    private readonly ServiceProvider provider;
    private readonly IVedicProfilesFacade facade;
    private static readonly CancellationToken Token = CancellationToken.None;

    public BirthTimeWindowTests()
    {
        accounts.GetRegisteredAccountIdAsync(user, Token).Returns(Guid.NewGuid());
        repository.CreateAsync(Arg.Any<Guid>(), Arg.Any<VedicProfileDetails>(), Token)
            .Returns(call => new VedicProfile(profileId, call.Arg<VedicProfileDetails>(), 1));
        var services = new ServiceCollection();
        services.AddSingleton(accounts);
        services.AddSingleton(repository);
        services.AddSingleton<TimeProvider>(new FixedClock());
        services.AddVedicProfilesBusiness();
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        facade = provider.GetRequiredService<IVedicProfilesFacade>();
    }

    public void Dispose() => provider.Dispose();

    private static VedicProfileDetails Valid() => new(
        "Asha", new DateOnly(1990, 4, 12), BirthTimeMode.Known,
        new TimeOnly(12, 34, 56), new TimeOnly(12, 34, 56), new TimeOnly(12, 34, 56),
        null, "Chennai", 13.0827m, 80.2707m, "Asia/Kolkata", "Female", "Engineer", Relationship.Self, null);

    private static VedicProfileDetails Window(TimeOnly from, TimeOnly to) =>
        Valid() with { BirthTimeMode = BirthTimeMode.Approximate, BirthTime = null, BirthTimeFrom = from, BirthTimeTo = to };

    [Fact]
    public async Task A_long_window_that_stays_within_the_day_is_valid()
    {
        // 00:00 -> 23:59 runs forward on the clock, so it is a legitimate (very wide) approximate window. It is
        // not a midnight crossing - no wrap occurs - and rejecting it would be a false positive on the rule.
        var details = Window(TimeOnly.MinValue, new TimeOnly(23, 59));

        var result = await facade.CreateAsync(user, details, Token);

        Assert.Equal(TimeOnly.MinValue, result.Details.BirthTimeFrom);
        Assert.Equal(new TimeOnly(23, 59), result.Details.BirthTimeTo);
    }

    [Fact]
    public async Task A_window_that_runs_forward_is_accepted_and_keeps_both_endpoints()
    {
        var details = Window(new TimeOnly(5, 0), new TimeOnly(7, 30));

        var result = await facade.CreateAsync(user, details, Token);

        Assert.Equal(new TimeOnly(5, 0), result.Details.BirthTimeFrom);
        Assert.Equal(new TimeOnly(7, 30), result.Details.BirthTimeTo);
        Assert.Null(result.Details.BirthTime);
    }

    [Theory]
    [InlineData(23, 30, 0, 30)]   // the reported bug: wraps midnight, previously accepted as a 23-hour window
    [InlineData(23, 59, 0, 0)]    // wraps at the boundary
    [InlineData(12, 0, 12, 0)]    // equal bounds: zero-length is not "approximate"
    [InlineData(12, 0, 11, 59)]   // backwards within a day
    public async Task A_window_that_does_not_run_forward_is_rejected_with_the_midnight_specific_error(
        int fromHour, int fromMinute, int toHour, int toMinute)
    {
        var details = Window(new TimeOnly(fromHour, fromMinute), new TimeOnly(toHour, toMinute));

        var failure = await Assert.ThrowsAsync<ValidationException>(() => facade.CreateAsync(user, details, Token));

        // The message must be specific: "the window must not cross midnight" is correctable advice, whereas the
        // generic invalid-times message would leave the user guessing which field is wrong.
        Assert.Equal(VedicProfileErrors.BirthWindowCrossesMidnight, failure.Message);
        await repository.DidNotReceive().CreateAsync(Arg.Any<Guid>(), Arg.Any<VedicProfileDetails>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_midnight_crossing_window_is_rejected_rather_than_clamped()
    {
        var details = Window(new TimeOnly(23, 30), new TimeOnly(0, 30));

        await Assert.ThrowsAsync<ValidationException>(() => facade.CreateAsync(user, details, Token));

        // Nothing was written, so no clamped or reinterpreted window can exist downstream.
        await repository.DidNotReceive().CreateAsync(Arg.Any<Guid>(), Arg.Any<VedicProfileDetails>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Exact_mode_still_requires_all_three_times_to_agree()
    {
        var mismatch = Valid() with { BirthTimeTo = new TimeOnly(12, 34, 57) };

        var failure = await Assert.ThrowsAsync<ValidationException>(() => facade.CreateAsync(user, mismatch, Token));

        // Not a window problem, so it must NOT be reported as a midnight crossing.
        Assert.Equal(VedicProfileErrors.InvalidTimes, failure.Message);
    }

    [Fact]
    public async Task Unknown_mode_carries_no_time_fields_at_all()
    {
        var details = Valid() with
        {
            BirthTimeMode = BirthTimeMode.Unknown, BirthTime = null, BirthTimeFrom = null, BirthTimeTo = null
        };

        var result = await facade.CreateAsync(user, details, Token);

        Assert.Null(result.Details.BirthTime);
        Assert.Null(result.Details.BirthTimeFrom);
        Assert.Null(result.Details.BirthTimeTo);
    }

    [Theory]
    [InlineData(BirthTimeMode.Unknown)]
    [InlineData(BirthTimeMode.Approximate)]
    public async Task Non_exact_modes_never_carry_a_single_birth_time(BirthTimeMode mode)
    {
        var details = mode == BirthTimeMode.Unknown
            ? Valid() with { BirthTimeMode = mode, BirthTime = null, BirthTimeFrom = null, BirthTimeTo = null }
            : Window(new TimeOnly(5, 0), new TimeOnly(7, 0));

        var result = await facade.CreateAsync(user, details, Token);

        // Absence, not a sentinel: nothing should have substituted noon or midnight.
        Assert.Null(result.Details.BirthTime);
    }
}
/// <summary>Local fixed clock: the clock type in the sibling test class is private to it.</summary>
internal sealed class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
}
