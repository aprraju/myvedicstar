using System.Net;
using Microsoft.Extensions.Logging;

namespace MyVedicStar.Authorize.Business.Users;

/// <summary>
/// Records a signed-in user's sign-in and returns their profile as carried by their access token.
/// </summary>
internal sealed partial class UserSignInService
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

        LogUserSignedIn(_logger, user.ExternalUserId);

        return new UserInfo(user.ExternalUserId, user.Name, user.Email);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in")]
    private static partial void LogUserSignedIn(ILogger logger, string userId);
}
