using System.Net;
using Microsoft.Extensions.Logging;
using MyVedicStar.Authorize.Integration.Identity;

namespace MyVedicStar.Authorize.Business.Users;

/// <summary>
/// Retrieves a signed-in user's profile from the identity provider and records the sign-in.
/// </summary>
internal sealed class UserSignInService
{
    private readonly IExternalIdentityService _externalIdentityService;
    private readonly IUserSignInRecorder _signInRecorder;
    private readonly ILogger<UserSignInService> _logger;

    public UserSignInService(
        IExternalIdentityService externalIdentityService,
        IUserSignInRecorder signInRecorder,
        ILogger<UserSignInService> logger)
    {
        _externalIdentityService = externalIdentityService;
        _signInRecorder = signInRecorder;
        _logger = logger;
    }

    public async Task<UserInfo> SignInAsync(string externalUserId, IPAddress? clientIpAddress, CancellationToken cancellationToken)
    {
        // Fetched before anything is written so no database transaction is held open during the remote call.
        var profile = await _externalIdentityService.GetUserProfileAsync(externalUserId, cancellationToken);

        var userId = await _signInRecorder.RecordSignInAsync(externalUserId, profile, clientIpAddress, cancellationToken);

        _logger.LogInformation("User {UserId} signed in", userId);

        return new UserInfo(
            userId,
            profile.GivenName,
            profile.Surname,
            DisplayNameFormatter.Format(profile.GivenName, profile.Surname),
            profile.Email);
    }
}
