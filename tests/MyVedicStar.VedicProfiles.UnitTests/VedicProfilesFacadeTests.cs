using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Business;
using MyVedicStar.VedicProfiles.Shared;
using NSubstitute;

namespace MyVedicStar.VedicProfiles.UnitTests;

public sealed class VedicProfilesFacadeTests : IDisposable
{
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly Guid accountId = Guid.NewGuid();
    private readonly Guid profileId = Guid.NewGuid();
    private readonly IVedicProfileAccountReader accounts = Substitute.For<IVedicProfileAccountReader>();
    private readonly IVedicProfileRepository repository = Substitute.For<IVedicProfileRepository>();
    private readonly ServiceProvider provider;
    private readonly IVedicProfilesFacade facade;
    private static readonly TimeOnly Noon = new(12, 34, 56);
    private static readonly CancellationToken Token = CancellationToken.None;

    public VedicProfilesFacadeTests()
    {
        accounts.GetRegisteredAccountIdAsync(user, Token).Returns(accountId);
        repository.CreateAsync(accountId, Arg.Any<VedicProfileDetails>(), Token)
            .Returns(call => new VedicProfile(profileId, call.Arg<VedicProfileDetails>(), 1));
        var services = new ServiceCollection();
        services.AddSingleton(accounts);
        services.AddSingleton(repository);
        services.AddSingleton<TimeProvider>(new FixedClock());
        services.AddVedicProfilesBusiness();
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        facade = provider.GetRequiredService<IVedicProfilesFacade>();
    }

    [Theory]
    [InlineData(BirthTimeMode.Known)]
    [InlineData(BirthTimeMode.Approximate)]
    [InlineData(BirthTimeMode.Unknown)]
    public async Task Valid_modes_preserve_local_details_without_conversion(BirthTimeMode mode)
    {
        var details = Valid() with
        {
            BirthTimeMode = mode,
            BirthTime = mode == BirthTimeMode.Known ? Noon : null,
            // An approximate window must run strictly forward (decision #16), so it cannot reuse the exact time as
            // both bounds - equal bounds are a zero-length window, which is an exact time claiming uncertainty.
            BirthTimeFrom = mode == BirthTimeMode.Unknown ? null : Noon,
            BirthTimeTo = mode switch
            {
                BirthTimeMode.Unknown => null,
                BirthTimeMode.Approximate => Noon.AddHours(2),
                _ => Noon
            }
        };
        var result = await facade.CreateAsync(user, details, Token);
        Assert.Equal(details, result.Details);
        Assert.Equal(1, result.Revision);
    }

    [Fact]
    public async Task Approximate_range_preserves_distinct_endpoints()
    {
        var details = Valid() with { BirthTimeMode = BirthTimeMode.Approximate, BirthTime = null, BirthTimeTo = Noon.AddHours(2) };
        Assert.Equal(details, (await facade.CreateAsync(user, details, Token)).Details);
    }

    public static IEnumerable<object[]> InvalidDetails()
    {
        var d = Valid();
        yield return [d with { BirthTimeFrom = null }];
        yield return [d with { BirthTimeTo = Noon.Add(TimeSpan.FromSeconds(1)) }];
        yield return [d with { BirthTime = null }];
        yield return [d with { BirthTimeMode = BirthTimeMode.Approximate, BirthTime = null, BirthTimeTo = null }];
        yield return [d with { BirthTimeMode = BirthTimeMode.Approximate, BirthTime = null, BirthTimeTo = Noon.Add(TimeSpan.FromSeconds(-1)) }];
        yield return [d with { BirthTimeMode = BirthTimeMode.Approximate }];
        yield return [d with { BirthTimeMode = BirthTimeMode.Unknown }];
        yield return [d with { BirthTime = Noon.Add(TimeSpan.FromTicks(1)), BirthTimeFrom = Noon.Add(TimeSpan.FromTicks(1)), BirthTimeTo = Noon.Add(TimeSpan.FromTicks(1)) }];
        yield return [d with { BirthTimeMode = (BirthTimeMode)999 }];
        yield return [d with { Relationship = (Relationship)999 }];
        yield return [d with { Relationship = Relationship.Other, CustomRelationship = null }];
        yield return [d with { CustomRelationship = "" }];
        yield return [d with { Name = " " }];
        yield return [d with { Name = new string('a', VedicProfileLimits.Name + 1) }];
        yield return [d with { Gender = " " }];
        yield return [d with { Gender = "text\ncontrol" }];
        yield return [d with { LocationName = "" }];
        yield return [d with { TimeAccuracyNote = new string('a', VedicProfileLimits.TimeAccuracyNote + 1) }];
        yield return [d with { Latitude = 90.000001m }];
        yield return [d with { Longitude = -180.000001m }];
        yield return [d with { TimezoneId = "Pacific Standard Time" }];
        yield return [d with { TimezoneId = "Not/AZone" }];
        yield return [d with { DateOfBirth = new DateOnly(2026, 11, 17) }];
    }

    [Theory]
    [MemberData(nameof(InvalidDetails))]
    public async Task Invalid_details_reject_without_any_write(VedicProfileDetails details)
    {
        await Assert.ThrowsAsync<ValidationException>(() => facade.CreateAsync(user, details, Token));
        await repository.DidNotReceive().CreateAsync(Arg.Any<Guid>(), Arg.Any<VedicProfileDetails>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Technical_bounds_coordinate_edges_and_UTC_date_are_accepted()
    {
        var details = Valid() with { DateOfBirth = new DateOnly(2026, 11, 16), Latitude = -90, Longitude = 180,
            Gender = "Any owner-provided gender", Name = new string('a', VedicProfileLimits.Name), TimezoneId = "UTC" };
        Assert.Equal(details, (await facade.CreateAsync(user, details, Token)).Details);
    }

    [Fact]
    public async Task Other_requires_custom_text_and_preserves_it()
    {
        var details = Valid() with { Relationship = Relationship.Other, CustomRelationship = "Family friend" };
        Assert.Equal(details, (await facade.CreateAsync(user, details, Token)).Details);
    }

    [Fact]
    public async Task Blank_self_create_uses_read_only_default_name()
    {
        accounts.GetDefaultNameAsync(user, Token).Returns("First Last");
        var result = await facade.CreateAsync(user, Valid() with { Relationship = Relationship.Self, Name = " " }, Token);
        Assert.Equal("First Last", result.Details.Name);
        await accounts.Received(1).GetDefaultNameAsync(user, Token);
    }

    [Fact]
    public async Task Missing_self_default_is_validation_error()
    {
        await Assert.ThrowsAsync<ValidationException>(() => facade.CreateAsync(user, Valid() with { Relationship = Relationship.Self, Name = null }, Token));
        await repository.DidNotReceive().CreateAsync(Arg.Any<Guid>(), Arg.Any<VedicProfileDetails>(), Token);
    }

    [Fact]
    public async Task Explicit_self_name_never_reads_account_name()
    {
        await facade.CreateAsync(user, Valid() with { Relationship = Relationship.Self }, Token);
        await accounts.DidNotReceive().GetDefaultNameAsync(Arg.Any<ClaimsPrincipal>(), Token);
    }

    [Fact]
    public async Task Reads_and_list_use_derived_account_and_never_refresh_saved_names()
    {
        var row = new VedicProfile(profileId, Valid(), 3);
        repository.GetAsync(accountId, profileId, Token).Returns(row);
        repository.ListAsync(accountId, Token).Returns(new[] { new VedicProfileSummary(profileId, "Saved", Relationship.Self, null, 3) });
        Assert.Equal(row, await facade.GetAsync(user, profileId, Token));
        Assert.Single(await facade.ListAsync(user, Token));
        await accounts.DidNotReceive().GetDefaultNameAsync(Arg.Any<ClaimsPrincipal>(), Token);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Missing_or_cross_user_replace_hides_revision_and_business_validation(long revision)
    {
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => facade.ReplaceAsync(user, profileId, Valid() with { Occupation = "" }, revision, Token));
        await repository.Received(1).GetAsync(accountId, profileId, Token);
        await repository.DidNotReceive().ReplaceAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<VedicProfileDetails>(), Arg.Any<long>(), Token);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Owned_replace_requires_positive_revision(long revision)
    {
        repository.GetAsync(accountId, profileId, Token).Returns(new VedicProfile(profileId, Valid(), 2));
        await Assert.ThrowsAsync<ValidationException>(() => facade.ReplaceAsync(user, profileId, Valid(), revision, Token));
    }

    [Fact]
    public async Task Stale_revision_is_conflict_without_write()
    {
        repository.GetAsync(accountId, profileId, Token).Returns(new VedicProfile(profileId, Valid(), 2));
        await Assert.ThrowsAsync<VedicProfileConflictException>(() => facade.ReplaceAsync(user, profileId, Valid(), 1, Token));
        await repository.DidNotReceive().ReplaceAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<VedicProfileDetails>(), Arg.Any<long>(), Token);
    }

    [Fact]
    public async Task Replace_passes_current_revision_and_preserves_explicit_name()
    {
        var details = Valid() with { Relationship = Relationship.Self };
        repository.GetAsync(accountId, profileId, Token).Returns(new VedicProfile(profileId, details, 2));
        repository.ReplaceAsync(accountId, profileId, details, 2, Token).Returns(new VedicProfile(profileId, details, 3));
        Assert.Equal(3, (await facade.ReplaceAsync(user, profileId, details, 2, Token)).Revision);
        await accounts.DidNotReceive().GetDefaultNameAsync(user, Token);
    }

    [Fact]
    public async Task Blank_self_replace_never_falls_back_to_account()
    {
        repository.GetAsync(accountId, profileId, Token).Returns(new VedicProfile(profileId, Valid(), 2));
        await Assert.ThrowsAsync<ValidationException>(() => facade.ReplaceAsync(user, profileId, Valid() with { Relationship = Relationship.Self, Name = "" }, 2, Token));
        await accounts.DidNotReceive().GetDefaultNameAsync(user, Token);
    }

    [Fact]
    public async Task Concurrent_self_conflict_propagates_safely()
    {
        repository.CreateAsync(accountId, Arg.Any<VedicProfileDetails>(), Token)
            .Returns<Task<VedicProfile>>(_ => throw new VedicProfileConflictException(VedicProfileErrors.DuplicateSelf));
        var error = await Assert.ThrowsAsync<VedicProfileConflictException>(() => facade.CreateAsync(user, Valid() with { Relationship = Relationship.Self }, Token));
        Assert.Equal(VedicProfileErrors.DuplicateSelf, error.PublicMessage);
    }

    [Fact]
    public async Task Missing_or_cross_user_get_and_delete_are_not_found()
    {
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => facade.GetAsync(user, profileId, Token));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => facade.DeleteAsync(user, profileId, Token));
        await repository.Received().DeleteAsync(accountId, profileId, Token);
    }

    [Fact]
    public async Task Delete_is_scoped_and_needs_no_revision()
    {
        repository.DeleteAsync(accountId, profileId, Token).Returns(true);
        await facade.DeleteAsync(user, profileId, Token);
        await repository.Received(1).DeleteAsync(accountId, profileId, Token);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_through_account_and_repository()
    {
        using var source = new CancellationTokenSource();
        accounts.GetRegisteredAccountIdAsync(user, source.Token).Returns(accountId);
        repository.GetAsync(accountId, profileId, source.Token).Returns<Task<VedicProfile?>>(_ => throw new OperationCanceledException(source.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => facade.GetAsync(user, profileId, source.Token));
    }

    private static VedicProfileDetails Valid() => new("Saved", new DateOnly(2000, 2, 29), BirthTimeMode.Known,
        Noon, Noon, Noon, null, "Birth location", 12.34567890123456789m, -76.12345678901234567m,
        "America/Chicago", "Prefer not to say", "Engineer", Relationship.Father, null);

    [Fact]
    public async Task Non_self_relationship_never_inherits_account_name()
    {
        // A non-Self profile must supply its own name; the account name is never substituted.
        await Assert.ThrowsAsync<ValidationException>(() => facade.CreateAsync(user, Valid() with { Relationship = Relationship.Father, Name = " " }, Token));
        await accounts.DidNotReceive().GetDefaultNameAsync(Arg.Any<ClaimsPrincipal>(), Token);
        await repository.DidNotReceive().CreateAsync(Arg.Any<Guid>(), Arg.Any<VedicProfileDetails>(), Token);
    }

    [Fact]
    public async Task Blank_self_name_falls_back_only_when_account_name_exists()
    {
        accounts.GetDefaultNameAsync(user, Token).Returns((string?)null);
        await Assert.ThrowsAsync<ValidationException>(() => facade.CreateAsync(user, Valid() with { Relationship = Relationship.Self, Name = "  " }, Token));
        await repository.DidNotReceive().CreateAsync(Arg.Any<Guid>(), Arg.Any<VedicProfileDetails>(), Token);
    }

    [Fact]
    public async Task Occupation_is_optional()
    {
        var result = await facade.CreateAsync(user, Valid() with { Occupation = "" }, Token);
        await repository.Received(1).CreateAsync(Arg.Any<Guid>(), Arg.Any<VedicProfileDetails>(), Token);
        Assert.Empty(result.Details.Occupation);
    }

    public void Dispose() => provider.Dispose();

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
    }
}
