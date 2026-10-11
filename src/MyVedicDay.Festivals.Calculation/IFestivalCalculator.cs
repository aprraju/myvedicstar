using MyVedicDay.Festivals.Calculation.Models;

namespace MyVedicDay.Festivals.Calculation;

/// <summary>
/// Finds the dates of festivals at a location. Pure and CPU-bound: no I/O, caching or persistence.
/// </summary>
public interface IFestivalCalculator
{
    /// <summary>Returns every occurrence between <see cref="FestivalCalculationRequest.From"/> and <see cref="FestivalCalculationRequest.To"/>, ordered by date.</summary>
    /// <exception cref="MyVedicDay.Astronomy.SunEventNotFoundException">The sun does not rise or set on a date at the location.</exception>
    /// <exception cref="MyVedicDay.Shared.Exceptions.CalculationException">A numerical search failed to converge.</exception>
    IReadOnlyList<FestivalOccurrence> Calculate(FestivalCalculationRequest request);
}
