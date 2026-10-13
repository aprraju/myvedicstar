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
    [Fact]
    public async Task Read_UsesVerifiedAccountAndDoesNotWrite()
    {
        var facade = Facade(); repository.ReadAsync(userId, default).Returns(new SettingsData(userId, null, null, 0, null));
        var result = await facade.GetAsync(user, default);
        Assert.Null(result.DefaultTradition); Assert.Null(result.DefaultLocationId);
        await repository.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default, default, default);
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
