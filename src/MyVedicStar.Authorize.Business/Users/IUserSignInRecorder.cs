using System.Net;

namespace MyVedicStar.Authorize.Business.Users;

/// <summary>
/// Stores the signed-in user's profile and records the sign-in. The implementation is chosen by the
/// <see cref="Options.FeatureFlagOptions.UserPersistence"/> feature flag.
/// </summary>
internal interface IUserSignInRecorder
{
    Task RecordSignInAsync(SignedInUser user, IPAddress? clientIpAddress, CancellationToken cancellationToken);
}
