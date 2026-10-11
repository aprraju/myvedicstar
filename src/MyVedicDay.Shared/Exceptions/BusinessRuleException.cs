namespace MyVedicDay.Shared.Exceptions;

/// <summary>
/// Raised when a valid request cannot be fulfilled because of a domain rule. The message is safe to return to API consumers.
/// </summary>
public sealed class BusinessRuleException : MyVedicDayException
{
    public BusinessRuleException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
