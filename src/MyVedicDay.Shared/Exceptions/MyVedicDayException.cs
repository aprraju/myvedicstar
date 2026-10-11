namespace MyVedicDay.Shared.Exceptions;

/// <summary>
/// Base type for exceptions raised intentionally by the platform so they can be mapped centrally to API responses.
/// </summary>
public abstract class MyVedicDayException : Exception
{
    protected MyVedicDayException(string message)
        : base(message)
    {
    }

    protected MyVedicDayException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
