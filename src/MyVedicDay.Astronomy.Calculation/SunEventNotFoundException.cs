using System.Globalization;
using MyVedicDay.Shared.Exceptions;
using NodaTime;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Raised when the sun does not rise or set on the requested date at the location (polar day or night),
/// so sunrise-based results (Panchang periods, Muhurtas) are undefined.
/// </summary>
public sealed class SunEventNotFoundException : CalculationException
{
    public SunEventNotFoundException(LocalDate date, string sunEvent)
        : base($"No {sunEvent} occurs on {date.ToString("uuuu-MM-dd", CultureInfo.InvariantCulture)} at the requested location.")
    {
    }
}
