namespace MyVedicDay.Shared.Exceptions;

/// <summary>
/// Raised when an external provider fails or returns an unusable response.
/// </summary>
public sealed class IntegrationException : MyVedicDayException
{
    public IntegrationException(string provider, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Provider = provider;
    }

    /// <summary>
    /// Logical name of the provider that failed. Never returned to API consumers.
    /// </summary>
    public string Provider { get; }
}
