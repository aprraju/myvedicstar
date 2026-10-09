namespace MyVedicStar.Shared.Exceptions;

/// <summary>Thrown when a requested resource does not exist.</summary>
public sealed class ResourceNotFoundException : MyVedicStarException
{
    public ResourceNotFoundException(string publicMessage, string? internalMessage = null, Exception? innerException = null)
        : base(publicMessage, internalMessage, innerException)
    {
    }
}
