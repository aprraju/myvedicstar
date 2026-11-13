using Microsoft.Extensions.Logging;

namespace MyVedicStar.Authorize.Business.Users;

internal sealed partial class DatabaseUserSignInRecorder
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Account defaults initialization failed ({FailureType}); next sign-in will retry.")]
    private static partial void LogAccountInitializationFailure(ILogger logger, string failureType);
}
