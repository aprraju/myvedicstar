namespace MyVedicStar.Shared.Exceptions;

/// <summary>Thrown when an external provider could not be reached or returned an unusable response.</summary>
public sealed class IntegrationException : MyVedicStarException
{
    public IntegrationException(string publicMessage, string? internalMessage = null, Exception? innerException = null)
        : base(publicMessage, internalMessage, innerException)
    {
    }
}
