using System.Net;
using Microsoft.Extensions.Logging;
using MyVedicStar.Authorize.Integration.Identity;

namespace MyVedicStar.Authorize.Business.Users;

/// <summary>
/// Used while the user-persistence feature flag is off: nothing is stored, and the identity provider's
/// object ID stands in for the internal user ID.
/// </summary>
internal sealed class DisabledUserSignInRecorder : IUserSignInRecorder
{
    private readonly ILogger<DisabledUserSignInRecorder> _logger;

    public DisabledUserSignInRecorder(ILogger<DisabledUserSignInRecorder> logger)
    {
        _logger = logger;
    }

    public Task<string> RecordSignInAsync(
        string externalUserId,
        ExternalUserProfile profile,
        IPAddress? clientIpAddress,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("User persistence is disabled by feature flag; profile and login were not stored");
        return Task.FromResult(externalUserId);
    }
}
