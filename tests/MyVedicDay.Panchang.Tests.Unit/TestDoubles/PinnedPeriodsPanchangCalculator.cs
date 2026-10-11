using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Panchang.Tests.Unit.TestDoubles;

/// <summary>
/// Decorates the real calculator and replaces the times of chosen period kinds, to reproduce a specific overlap.
/// </summary>
internal sealed class PinnedPeriodsPanchangCalculator(IReadOnlyDictionary<PanchangPeriodKind, (Instant Start, Instant End)> pinned)
    : IPanchangCalculator
{
    private readonly IPanchangCalculator _inner =
        new ServiceCollection().AddPanchangCalculation().BuildServiceProvider().GetRequiredService<IPanchangCalculator>();

    public PanchangDay Calculate(PanchangCalculationRequest request)
    {
        var day = _inner.Calculate(request);
        return day with
        {
            Periods = day.Periods
                .Select(period => pinned.TryGetValue(period.Kind, out var times) ? period with { Start = times.Start, End = times.End } : period)
                .ToArray(),
        };
    }
}
