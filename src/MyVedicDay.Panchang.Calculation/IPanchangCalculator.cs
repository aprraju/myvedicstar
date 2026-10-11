using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Calculation.Models;

namespace MyVedicDay.Panchang.Calculation;

/// <summary>
/// Calculates the Panchang for a single local date. Pure and CPU-bound: no I/O, caching or persistence.
/// </summary>
public interface IPanchangCalculator
{
    /// <summary>
    /// Calculates sunrise/sunset, Tithi, Nakshatra, Yoga, Karana and the tradition's time periods.
    /// </summary>
    /// <exception cref="SunEventNotFoundException">The sun does not rise or set on the date at the location.</exception>
    /// <exception cref="MyVedicDay.Shared.Exceptions.CalculationException">A numerical search failed to converge.</exception>
    PanchangDay Calculate(PanchangCalculationRequest request);
}
