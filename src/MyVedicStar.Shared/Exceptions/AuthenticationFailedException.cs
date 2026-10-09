namespace MyVedicStar.Shared.Exceptions;

/// <summary>Thrown when the caller's identity could not be established (for example, an invalid token).</summary>
public sealed class AuthenticationFailedException : MyVedicStarException
{
    public AuthenticationFailedException(string publicMessage, string? internalMessage = null, Exception? innerException = null)
        : base(publicMessage, internalMessage, innerException)
    {
    }
}
