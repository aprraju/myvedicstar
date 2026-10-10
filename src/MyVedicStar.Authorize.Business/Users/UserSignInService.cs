using System.Net;
using Microsoft.Extensions.Logging;

namespace MyVedicStar.Authorize.Business.Users;

/// <summary>
/// Records a signed-in user's sign-in and returns their profile as carried by their access token.
/// </summary>
internal sealed class UserSignInService
{
    private readonly IUserSignInRecorder _signInRecorder;
    private readonly ILogger<UserSignInService> _logger;

    public UserSignInService(IUserSignInRecorder signInRecorder, ILogger<UserSignInService> logger)
    {
        _signInRecorder = signInRecorder;
        _logger = logger;
    }

    public async Task<UserInfo> SignInAsync(SignedInUser user, IPAddress? clientIpAddress, CancellationToken cancellationToken)
    {
        await _signInRecorder.RecordSignInAsync(user, clientIpAddress, cancellationToken);

        _logger.LogInformation("User {UserId} signed in", user.ExternalUserId);

        return new UserInfo(user.ExternalUserId, user.Name, user.Email);
    }
}
