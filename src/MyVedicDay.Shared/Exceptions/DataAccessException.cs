namespace MyVedicDay.Shared.Exceptions;

/// <summary>
/// Raised when a persistence operation fails.
/// </summary>
public sealed class DataAccessException : MyVedicDayException
{
    public DataAccessException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
