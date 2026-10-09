using Microsoft.Extensions.Logging.Abstractions;
using MyVedicStar.Authorize.Business.Users;
using MyVedicStar.Authorize.Integration.Identity;

namespace MyVedicStar.Authorize.Tests.Unit.Business;

public sealed class DisabledUserSignInRecorderTests
{
    [Fact]
    public async Task RecordSignInAsync_ReturnsExternalObjectIdAsUserId()
    {
        var recorder = new DisabledUserSignInRecorder(NullLogger<DisabledUserSignInRecorder>.Instance);
        var profile = new ExternalUserProfile("object-id", null, null, null);

        var userId = await recorder.RecordSignInAsync("object-id", profile, clientIpAddress: null, CancellationToken.None);

        Assert.Equal("object-id", userId);
    }
}
