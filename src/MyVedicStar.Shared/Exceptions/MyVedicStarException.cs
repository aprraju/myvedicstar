namespace MyVedicStar.Shared.Exceptions;

/// <summary>
/// Base type for all application exceptions that are mapped centrally to Problem Details responses.
/// </summary>
/// <remarks>
/// <see cref="Exception.Message"/> is for logs only. The API returns <see cref="PublicMessage"/>,
/// which must never contain implementation details.
/// </remarks>
public abstract class MyVedicStarException : Exception
{
    protected MyVedicStarException(string publicMessage, string? internalMessage = null, Exception? innerException = null)
        : base(internalMessage ?? publicMessage, innerException)
    {
        PublicMessage = publicMessage;
    }

    /// <summary>Consumer-safe description of the failure.</summary>
    public string PublicMessage { get; }
}
