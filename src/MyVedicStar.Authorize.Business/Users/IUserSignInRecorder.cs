using System.Net;

namespace MyVedicStar.Authorize.Business.Users;

/// <summary>The names to return for a signed-in user: the stored account's when persisted, otherwise the token's.</summary>
internal sealed record SignedInUserNames(string? DisplayName, string? FirstName, string? LastName);

/// <summary>
/// Stores the signed-in user's profile and records the sign-in. The implementation is chosen by the
/// <see cref="Options.FeatureFlagOptions.UserPersistence"/> feature flag.
/// </summary>
internal interface IUserSignInRecorder
{
    Task<SignedInUserNames> RecordSignInAsync(SignedInUser user, IPAddress? clientIpAddress, CancellationToken cancellationToken);
    Task RecordSignOutAsync(SignedInUser user, IPAddress? clientIpAddress, CancellationToken cancellationToken);
}
