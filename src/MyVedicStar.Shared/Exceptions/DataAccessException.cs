namespace MyVedicStar.Shared.Exceptions;

/// <summary>Thrown when data could not be read from or written to the database.</summary>
public sealed class DataAccessException : MyVedicStarException
{
    public DataAccessException(string publicMessage, string? internalMessage = null, Exception? innerException = null)
        : base(publicMessage, internalMessage, innerException)
    {
    }
}
