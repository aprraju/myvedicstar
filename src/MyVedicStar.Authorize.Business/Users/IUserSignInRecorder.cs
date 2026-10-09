using System.Net;
using MyVedicStar.Authorize.Integration.Identity;

namespace MyVedicStar.Authorize.Business.Users;

/// <summary>
/// Stores the signed-in user's profile and records the sign-in. The implementation is chosen by the
/// <see cref="Options.FeatureFlagOptions.UserPersistence"/> feature flag.
/// </summary>
internal interface IUserSignInRecorder
{
    /// <returns>The user ID to expose to the client.</returns>
    Task<string> RecordSignInAsync(
        string externalUserId,
        ExternalUserProfile profile,
        IPAddress? clientIpAddress,
        CancellationToken cancellationToken);
}
