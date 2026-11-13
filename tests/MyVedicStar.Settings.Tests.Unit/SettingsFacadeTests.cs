using System.Security.Claims;
using MyVedicDay.Traditions;
using MyVedicStar.Authorize.Business;
using MyVedicStar.Settings.Business;
using MyVedicStar.Settings.Data;
using MyVedicStar.Settings.Integration;
using MyVedicStar.Shared.Exceptions;
using NSubstitute;
namespace MyVedicStar.Settings.Tests.Unit;

public sealed class SettingsFacadeTests
{
    private static readonly string[] TraditionKeys = ["telugu", "tamil"];
    private readonly Guid userId = Guid.NewGuid();
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly IAuthorizeFacade accounts = Substitute.For<IAuthorizeFacade>();
    private readonly ISettingsRepository repository = Substitute.For<ISettingsRepository>();
    private readonly ITraditionRegistry traditions = Substitute.For<ITraditionRegistry>();
    private readonly ISavedLocationValidator validator = Substitute.For<ISavedLocationValidator>();
    private SettingsFacade Facade()
    {
        accounts.GetRegisteredAccountIdAsync(user, Arg.Any<CancellationToken>()).Returns(userId);
        traditions.GetKeysAsync(Arg.Any<CancellationToken>()).Returns(TraditionKeys);
        return new(accounts, repository, traditions, validator);
    }
    public static IEnumerable<object[]> Combinations() =>
        from chart in PreferenceCodes.ChartStyles from ayanamsa in PreferenceCodes.Ayanamsas select new object[] { chart, ayanamsa };

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task Save_AllTwelveCombinationsAreCanonicalAndAtomic(string chart, string ayanamsa)
    {
        var facade = Facade();
        repository.SaveAsync(userId, "telugu", null, 4, default, chart, ayanamsa)
            .Returns(new SettingsData(userId, "telugu", null, 5, null, chart, ayanamsa));
        var result = await facade.SaveAsync(user, new("telugu", null, 4, chart.ToUpperInvariant(), ayanamsa.ToUpperInvariant()), default);
        Assert.Equal(chart, result.ChartStyle); Assert.Equal(ayanamsa, result.Ayanamsa); Assert.Equal(5, result.Revision);
        await repository.Received(1).SaveAsync(userId, "telugu", null, 4, default, chart, ayanamsa);
    }
    [Theory]
    [InlineData("")] [InlineData(" ")] [InlineData("north indian")] [InlineData("south-indian ")] [InlineData("unknown")]
    public async Task Save_InvalidPreferenceRejectsBeforeAnyPersistence(string invalid)
    {
        var facade = Facade();
        var chartError = await Assert.ThrowsAsync<ValidationException>(() => facade.SaveAsync(user, new("tamil", null, 0, invalid, "lahiri"), default));
        Assert.Equal(PreferenceCodes.InvalidChartStyle, chartError.PublicMessage);
        var ayanamsaError = await Assert.ThrowsAsync<ValidationException>(() => facade.SaveAsync(user, new("tamil", null, 0, "south-indian", invalid), default));
        Assert.Equal(PreferenceCodes.InvalidAyanamsa, ayanamsaError.PublicMessage);
        Assert.Empty(repository.ReceivedCalls());
    }
    [Theory]
    [InlineData(null, "raman")] [InlineData("north-indian", "true-chitra")] [InlineData("east-indian", "krishnamurti")]
    public async Task Save_ChangesOneOrBothWithoutTouchingOtherAccounts(string? chart, string? ayanamsa)
    {
        var facade = Facade();
        repository.SaveAsync(userId, "telugu", null, 1, default, chart, ayanamsa)
            .Returns(new SettingsData(userId, "telugu", null, 2, null, chart ?? "south-indian", ayanamsa ?? "lahiri"));
        await facade.SaveAsync(user, new("telugu", null, 1, chart, ayanamsa), default);
        await repository.Received(1).SaveAsync(userId, "telugu", null, 1, default, chart, ayanamsa);
        await repository.DidNotReceive().SaveAsync(Arg.Is<Guid>(id => id != userId), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }
    [Theory]
    [InlineData(null, null)] [InlineData("east-indian", null)] [InlineData(null, "raman")]
    public async Task Save_OmittedCounterpartIsPassedAsUnchanged(string? chart, string? ayanamsa)
    {
        var facade = Facade();
        repository.SaveAsync(userId, null, null, 3, default, chart, ayanamsa)
            .Returns(new SettingsData(userId, null, null, 4, null, chart ?? "north-indian", ayanamsa ?? "true-chitra"));
        var result = await facade.SaveAsync(user, new(null, null, 3, chart, ayanamsa), default);
        Assert.Equal(chart ?? "north-indian", result.ChartStyle); Assert.Equal(ayanamsa ?? "true-chitra", result.Ayanamsa);
    }
    [Fact]
    public async Task LifecycleHandlerDelegatesToSettingsOwnedIdempotentPort()
    {
        await new AccountSettingsSignInHandler(repository).EnsureAccountDefaultsAsync(userId, default);
        await repository.Received(1).EnsureAsync(userId, default);
    }
    [Fact]
    public async Task Read_UsesVerifiedAccountAndDoesNotWrite()
    {
        var facade = Facade(); repository.ReadAsync(userId, default).Returns(new SettingsData(userId, null, null, 0, null));
        var result = await facade.GetAsync(user, default);
        Assert.Null(result.DefaultTradition); Assert.Null(result.DefaultLocationId);
        await repository.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default, default, default);
    }
    [Fact]
    public async Task Get_NoRowReturnsDefaultsAtRevisionZeroAndNeverCreatesOne()
    {
        var facade = Facade();
        repository.ReadAsync(userId, default).Returns(new SettingsData(userId, null, null, 0, null));
        var result = await facade.GetAsync(user, default);
        Assert.Equal("south-indian", result.ChartStyle); Assert.Equal("lahiri", result.Ayanamsa); Assert.Equal(0, result.Revision);
        await repository.Received(1).ReadAsync(userId, default);
        Assert.Single(repository.ReceivedCalls());
    }
    [Theory]
    [InlineData("unsupported")]
    [InlineData("../../telugu")]
    public async Task Save_RejectsUnsupportedTradition(string value)
    {
        var facade = Facade();
        await Assert.ThrowsAsync<ValidationException>(() => facade.SaveAsync(user, new(value, null, 0), default));
        await repository.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default, default, default);
    }
    [Fact]
    public async Task Save_CanonicalizesTraditionAndPreservesLocationChoice()
    {
        var locationId = Guid.NewGuid(); var facade = Facade();
        repository.SaveAsync(userId, "tamil", locationId, 2, default).Returns(new SettingsData(userId, "tamil", locationId, 3, null));
        var result = await facade.SaveAsync(user, new("TAMIL", locationId, 2), default);
        Assert.Equal("tamil", result.DefaultTradition); Assert.Equal(locationId, result.DefaultLocationId);
    }
    [Fact]
    public async Task Add_ValidatesTrustedTimezoneAndDoesNotChangeDefaults()
    {
        var facade = Facade(); validator.HasMatchingTimeZoneAsync(30.2672, -97.7431, "America/Chicago", default).Returns(true);
        repository.AddAsync(Arg.Any<SavedLocationData>(), default).Returns(info => ((SavedLocationData)info[0]) with { LocationId = Guid.NewGuid() });
        var result = await facade.AddAsync(user, new("Austin", "Texas", "United States", "US", "78701", 30.2672, -97.7431, "America/Chicago"), default);
        Assert.NotEqual(Guid.Empty, result.LocationId);
        await repository.Received().AddAsync(Arg.Is<SavedLocationData>(x => x.UserId == userId), default);
        await repository.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default, default, default);
    }
    [Fact]
    public async Task Add_RejectsTimezoneMismatch()
    {
        var facade = Facade();
        await Assert.ThrowsAsync<ValidationException>(() => facade.AddAsync(user, new(null, null, null, null, null, 30, -97, "Asia/Kolkata"), default));
        await repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    [InlineData(double.NaN, 0)]
    public async Task Add_RejectsInvalidCoordinates(double latitude, double longitude)
    {
        var facade = Facade();
        await Assert.ThrowsAsync<ValidationException>(() => facade.AddAsync(user, new(null, null, null, null, null, latitude, longitude, "UTC"), default));
    }
    [Fact]
    public async Task Delete_ScopesForeignLocationIdToAuthenticatedAccount()
    {
        var facade = Facade(); var foreignId = Guid.NewGuid();
        repository.DeleteAsync(userId, foreignId, default).Returns(new SettingsData(userId, "telugu", null, 2, null));
        await facade.DeleteAsync(user, foreignId, default);
        await repository.Received(1).DeleteAsync(userId, foreignId, default);
    }
}
