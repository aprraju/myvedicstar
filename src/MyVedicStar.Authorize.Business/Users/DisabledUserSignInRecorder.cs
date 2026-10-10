using System.Net;
using Microsoft.Extensions.Logging;

namespace MyVedicStar.Authorize.Business.Users;

/// <summary>
/// Used while the user-persistence feature flag is off: nothing is stored.
/// </summary>
internal sealed class DisabledUserSignInRecorder : IUserSignInRecorder
{
    private readonly ILogger<DisabledUserSignInRecorder> _logger;

    public DisabledUserSignInRecorder(ILogger<DisabledUserSignInRecorder> logger)
    {
        _logger = logger;
    }

    public Task RecordSignInAsync(SignedInUser user, IPAddress? clientIpAddress, CancellationToken cancellationToken)
    {
        _logger.LogWarning("User persistence is disabled by feature flag; profile and login were not stored");
        return Task.CompletedTask;
    }
}
