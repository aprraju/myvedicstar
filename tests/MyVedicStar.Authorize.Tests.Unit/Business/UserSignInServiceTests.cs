using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using MyVedicStar.Authorize.Business;
using MyVedicStar.Authorize.Business.Users;
using MyVedicStar.Shared.Exceptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MyVedicStar.Authorize.Tests.Unit.Business;

public sealed class UserSignInServiceTests
{
    private static readonly IPAddress ClientIp = IPAddress.Parse("203.0.113.7");

    private static readonly SignedInUser User = new("object-id", "John", "Smith", "john.smith@example.com", DisplayName: "Token Display");

    private readonly IUserSignInRecorder _signInRecorder = Substitute.For<IUserSignInRecorder>();
    private readonly UserSignInService _service;

    public UserSignInServiceTests()
    {
        _signInRecorder.RecordSignInAsync(User, ClientIp, Arg.Any<CancellationToken>()).Returns(new SignedInUserNames("Stored Display", "Jon", "Smyth"));
        _service = new UserSignInService(_signInRecorder, NullLogger<UserSignInService>.Instance);
    }

    [Fact]
    public async Task SignInAsync_RecordsTokenUserWithClientIp()
    {
        await _service.SignInAsync(User, ClientIp, CancellationToken.None);

        await _signInRecorder.Received(1).RecordSignInAsync(User, ClientIp, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignInAsync_ReturnsStoredDisplayNameAsNameWithTokenIdAndEmail()
    {
        var userInfo = await _service.SignInAsync(User, ClientIp, CancellationToken.None);

        Assert.Equal(new UserInfo("object-id", "Stored Display", "john.smith@example.com", "Jon", "Smyth"), userInfo);
    }

    [Fact]
    public async Task SignInAsync_RecordingFails_Throws()
    {
        _signInRecorder.RecordSignInAsync(User, ClientIp, Arg.Any<CancellationToken>())
            .ThrowsAsync(new DataAccessException("could not save"));

        await Assert.ThrowsAsync<DataAccessException>(() => _service.SignInAsync(User, ClientIp, CancellationToken.None));
    }
}
