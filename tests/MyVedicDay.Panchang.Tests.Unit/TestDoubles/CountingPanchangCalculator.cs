using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Calculation.Models;

namespace MyVedicDay.Panchang.Tests.Unit.TestDoubles;

/// <summary>
/// Decorates the real calculator to count how many daily calculations were performed.
/// </summary>
internal sealed class CountingPanchangCalculator(IPanchangCalculator inner) : IPanchangCalculator
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public PanchangDay Calculate(PanchangCalculationRequest request)
    {
        Interlocked.Increment(ref _calls);
        return inner.Calculate(request);
    }
}
