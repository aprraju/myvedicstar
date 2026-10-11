namespace MyVedicDay.Shared.Exceptions;

/// <summary>
/// Raised when caller-supplied input is invalid. Error messages are safe to return to API consumers.
/// </summary>
public sealed class ValidationException : MyVedicDayException
{
    public ValidationException(string parameterName, string message)
        : this(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [parameterName] = [message] })
    {
    }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    /// <summary>
    /// Validation messages keyed by the offending parameter name.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
