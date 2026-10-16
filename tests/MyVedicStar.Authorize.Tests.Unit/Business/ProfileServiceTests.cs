using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Business;
using MyVedicStar.Authorize.Business.Options;
using MyVedicStar.Authorize.Business.Users;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Authorize.Integration;
using MyVedicStar.Shared.Exceptions;
using NSubstitute;
namespace MyVedicStar.Authorize.Tests.Unit.Business;

public sealed class ProfileServiceTests
{
    private readonly string oid = Guid.NewGuid().ToString();
    private const string Issuer = "https://tenant.ciamlogin.com/tenant/v2.0";
    private readonly IProfileRepository repository = Substitute.For<IProfileRepository>();
    private readonly IGraphProfileClient graph = Substitute.For<IGraphProfileClient>();
    private ClaimsPrincipal User() => new(new ClaimsIdentity([new("iss", Issuer), new("oid", oid)], "test"));
    private static ProfileSyncWork Work(bool pending, bool displayNamePending = false) => new(new ManagedProfile(Guid.NewGuid(), "Raju", "Bhupathi", "Raju B", null, null, "US", null, null, null, "a@example.com", null, 2, pending || displayNamePending), pending, false, displayNamePending);
    private ProfileService Service() => new(repository, new SignedInUserReader(Options.Create(new UserIdentityClaimOptions { ExternalUserId = ["oid"], Email = ["email"] })), graph, NullLogger<ProfileService>.Instance);
    [Fact]
    public async Task Read_DoesNotCreateAnAccountOrSynchronizeNames()
    {
        var work = Work(false); repository.ReadAsync(Issuer, oid, default).Returns(work);
        Assert.Equal(work.Profile, await Service().ReadAsync(User(), default));
        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default!, default, default!, default);
        await graph.DidNotReceiveWithAnyArgs().UpdateNamesAsync(default!, default!, default!, default);
    }
    [Fact]
    public async Task Update_UnchangedNamesNeverContactGraph()
    {
        var work = Work(false); repository.ReadAsync(Issuer, oid, default).Returns(work);
        repository.UpdateAsync(Issuer, oid, 2, Arg.Any<IReadOnlyDictionary<string, string?>>(), default).Returns(work);
        await Service().UpdateAsync(User(), new(2, new() { ["streetAddress"] = "1 Main Street" }), default);
        await graph.DidNotReceiveWithAnyArgs().UpdateNamesAsync(default!, default!, default!, default);
    }
    [Fact]
    public async Task Retry_SendsOnlyPendingAttributeToVerifiedObject()
    {
        var work = Work(true); repository.ReadAsync(Issuer, oid, default).Returns(work);
        repository.SynchronizeAsync(Issuer, oid, Arg.Any<Func<ProfileSyncWork, CancellationToken, Task>>(), default).Returns(async info =>
        {
            await info.Arg<Func<ProfileSyncWork, CancellationToken, Task>>()(work, default);
            return work.Profile with { NameSyncPending = false };
        });
        var result = await Service().RetrySyncAsync(User(), default);
        Assert.False(result.NameSyncPending);
        await graph.Received(1).UpdateNamesAsync(Issuer, oid, Arg.Is<IReadOnlyDictionary<string, string?>>(x => x.Count == 1 && x["givenName"] == "Raju"), default);
    }
    [Fact]
    public async Task Update_GraphFailureReturnsDurablePartialSuccess()
    {
        var work = Work(true); repository.ReadAsync(Issuer, oid, default).Returns(work);
        repository.UpdateAsync(Issuer, oid, 2, Arg.Any<IReadOnlyDictionary<string, string?>>(), default).Returns(work);
        repository.SynchronizeAsync(Issuer, oid, Arg.Any<Func<ProfileSyncWork, CancellationToken, Task>>(), default)
            .Returns<Task<ManagedProfile>>(_ => throw new IntegrationException("Unavailable"));
        var result = await Service().UpdateAsync(User(), new(2, new() { ["firstName"] = "Raju" }), default);
        Assert.True(result.NameSyncPending); Assert.Equal("Raju", result.FirstName);
    }
    [Fact]
    public async Task Retry_PendingDisplayName_SendsDisplayNameOnly()
    {
        var work = Work(false, displayNamePending: true); repository.ReadAsync(Issuer, oid, default).Returns(work);
        repository.SynchronizeAsync(Issuer, oid, Arg.Any<Func<ProfileSyncWork, CancellationToken, Task>>(), default).Returns(async info =>
        {
            await info.Arg<Func<ProfileSyncWork, CancellationToken, Task>>()(work, default);
            return work.Profile with { NameSyncPending = false };
        });
        await Service().RetrySyncAsync(User(), default);
        await graph.Received(1).UpdateNamesAsync(Issuer, oid, Arg.Is<IReadOnlyDictionary<string, string?>>(x => x.Count == 1 && x["displayName"] == "Raju B"), default);
    }

    [Fact]
    public async Task Update_DisplayNameIsTrimmedAndPassedToTheRepository()
    {
        var work = Work(false, displayNamePending: true); repository.ReadAsync(Issuer, oid, default).Returns(work);
        repository.UpdateAsync(Issuer, oid, 2, Arg.Any<IReadOnlyDictionary<string, string?>>(), default).Returns(work);
        await Service().UpdateAsync(User(), new(2, new() { ["firstName"] = "Raju", ["lastName"] = "Bhupathi", ["displayName"] = "  Raju B  " }), default);
        await repository.Received(1).UpdateAsync(Issuer, oid, 2, Arg.Is<IReadOnlyDictionary<string, string?>>(x =>
            x["displayName"] == "Raju B" && x["firstName"] == "Raju" && x["lastName"] == "Bhupathi"), default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_RejectsClearingDisplayName(string? value) =>
        Assert.Throws<ValidationException>(() => ProfileService.Validate(new Dictionary<string, string?> { ["displayName"] = value }));

    [Theory]
    [InlineData("displayName", "Line\nBreak")]
    public void Validate_RejectsControlCharacters(string field, string escaped) =>
        Assert.Throws<ValidationException>(() => ProfileService.Validate(new Dictionary<string, string?> { [field] = System.Text.RegularExpressions.Regex.Unescape(escaped) }));

    [Fact]
    public void Validate_RejectsOverlongDisplayName() =>
        Assert.Throws<ValidationException>(() => ProfileService.Validate(new Dictionary<string, string?> { ["displayName"] = new string('d', 257) }));

    [Theory]
    [InlineData("userId", "other-account")]
    [InlineData("email", "invalid")]
    [InlineData("phoneNumber", "abcdef")]
    public void Validate_RejectsUnownedFieldsAndInvalidInput(string field, string value) =>
        Assert.Throws<ValidationException>(() => ProfileService.Validate(new Dictionary<string, string?> { [field] = value }));
}
