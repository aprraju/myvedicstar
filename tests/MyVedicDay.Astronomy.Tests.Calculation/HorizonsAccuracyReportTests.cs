using System.Text.Json;
using Xunit.Abstractions;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// Emits the AST-09 residual report. Not an assertion - the number a reviewer reads, produced by the same code path
/// <see cref="HorizonsAccuracyTests"/> measures, so the report and the gate cannot disagree.
/// </summary>
public sealed class HorizonsAccuracyReportTests(ITestOutputHelper output)
{
    [Fact]
    public void Prints_the_residual_distribution()
    {
        var report = HorizonsAccuracyTests.MeasureForReport();
        foreach (var entry in report.OrderBy(entry => entry.Body).ThenBy(entry => entry.JulianDayUt))
        {
            output.WriteLine($"{entry.Body,-8} jd={entry.JulianDayUt:F1} " +
                             $"ref={entry.Reference:F7} engine={(entry.Engine is null ? "n/a" : entry.Engine.Value.ToString("F7"))} " +
                             $"residual={(entry.ResidualArcseconds is null ? "n/a" : entry.ResidualArcseconds.Value.ToString("F3"))} arcsec");
        }
    }
}
