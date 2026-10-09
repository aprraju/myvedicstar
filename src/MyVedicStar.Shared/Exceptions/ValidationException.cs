namespace MyVedicStar.Shared.Exceptions;

/// <summary>Thrown when caller-supplied input is invalid.</summary>
public sealed class ValidationException : MyVedicStarException
{
    public ValidationException(string publicMessage, string? internalMessage = null)
        : base(publicMessage, internalMessage)
    {
    }
}
