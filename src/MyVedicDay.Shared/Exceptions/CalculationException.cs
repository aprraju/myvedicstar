namespace MyVedicDay.Shared.Exceptions;

/// <summary>
/// Raised when an astronomical or Panchang calculation cannot be completed.
/// </summary>
public class CalculationException : MyVedicDayException
{
    public CalculationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
