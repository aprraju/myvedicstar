using System.Net;
using Microsoft.Extensions.Logging;

namespace MyVedicStar.Authorize.Business.Users;

/// <summary>
/// Used while the user-persistence feature flag is off: nothing is stored.
/// </summary>
internal sealed partial class DisabledUserSignInRecorder : IUserSignInRecorder
{
    private readonly ILogger<DisabledUserSignInRecorder> _logger;

    public DisabledUserSignInRecorder(ILogger<DisabledUserSignInRecorder> logger)
    {
        _logger = logger;
    }

    public Task RecordSignInAsync(SignedInUser user, IPAddress? clientIpAddress, CancellationToken cancellationToken)
    {
        LogPersistenceDisabled(_logger);
        return Task.CompletedTask;
    }

    // Debug, not Warning: skipping persistence is the configured behaviour, not a fault, and happens on every sign-in.
    [LoggerMessage(Level = LogLevel.Debug, Message = "User persistence is disabled by feature flag; profile and login were not stored")]
    private static partial void LogPersistenceDisabled(ILogger logger);
}
