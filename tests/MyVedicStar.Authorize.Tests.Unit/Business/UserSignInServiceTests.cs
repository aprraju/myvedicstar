using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using MyVedicStar.Authorize.Business;
using MyVedicStar.Authorize.Business.Users;
using MyVedicStar.Authorize.Integration.Identity;
using MyVedicStar.Shared.Exceptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MyVedicStar.Authorize.Tests.Unit.Business;

public sealed class UserSignInServiceTests
{
    private const string ExternalUserId = "object-id";
    private const string RecordedUserId = "0199a3b4-0000-7000-8000-000000000001";
    private static readonly IPAddress ClientIp = IPAddress.Parse("203.0.113.7");

    private static readonly ExternalUserProfile GraphProfile = new(
        ExternalUserId, "john.smith@example.com", "John", "Smith");

    private readonly IExternalIdentityService _externalIdentityService = Substitute.For<IExternalIdentityService>();
    private readonly IUserSignInRecorder _signInRecorder = Substitute.For<IUserSignInRecorder>();
    private readonly UserSignInService _service;

    public UserSignInServiceTests()
    {
        _externalIdentityService.GetUserProfileAsync(ExternalUserId, Arg.Any<CancellationToken>()).Returns(GraphProfile);
        _signInRecorder.RecordSignInAsync(ExternalUserId, GraphProfile, ClientIp, Arg.Any<CancellationToken>()).Returns(RecordedUserId);

        _service = new UserSignInService(_externalIdentityService, _signInRecorder, NullLogger<UserSignInService>.Instance);
    }

    [Fact]
    public async Task SignInAsync_RecordsGraphProfileWithTokenObjectIdAndClientIp()
    {
        await _service.SignInAsync(ExternalUserId, ClientIp, CancellationToken.None);

        await _signInRecorder.Received(1).RecordSignInAsync(ExternalUserId, GraphProfile, ClientIp, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignInAsync_ReturnsMinimalUserInfoWithRecordedIdAndDisplayName()
    {
        var userInfo = await _service.SignInAsync(ExternalUserId, ClientIp, CancellationToken.None);

        Assert.Equal(new UserInfo(RecordedUserId, "John", "Smith", "John Smith", "john.smith@example.com"), userInfo);
    }

    [Fact]
    public async Task SignInAsync_GraphFails_RecordsNothing()
    {
        _externalIdentityService.GetUserProfileAsync(ExternalUserId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new IntegrationException("unavailable"));

        await Assert.ThrowsAsync<IntegrationException>(() => _service.SignInAsync(ExternalUserId, ClientIp, CancellationToken.None));

        await _signInRecorder.DidNotReceiveWithAnyArgs().RecordSignInAsync(default!, default!, default, default);
    }
}
