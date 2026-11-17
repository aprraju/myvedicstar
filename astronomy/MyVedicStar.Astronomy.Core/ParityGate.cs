using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Core;

/// <summary>
/// One reference case for the analytic-vs-SPICE parity gate. The fixtures are checked in as data (architect note 3),
/// so the comparison exists before AST-06 needs it rather than being discovered there.
/// </summary>
/// <param name="Body">Body under test.</param>
/// <param name="JulianDayUt">Instant, Julian Day UT.</param>
/// <param name="TropicalLongitudeDegrees">Reference tropical longitude of date, degrees.</param>
/// <param name="Source">Where the reference value came from, so it is attributable rather than assumed.</param>
/// <param name="ToleranceArcseconds">Tolerance this case must meet. A case may tighten the global budget, never
/// loosen it beyond what AST-09 permits.</param>
public sealed record ParityFixture(
    CelestialBody Body,
    double JulianDayUt,
    double TropicalLongitudeDegrees,
    string Source,
    double ToleranceArcseconds);

/// <summary>
/// The parity gate's comparison rules. Deliberately a first-class type with a declared global budget rather than a
/// loose assertion inside one test, because AST-06 must prove parity before any default flip and a soft target would
/// not survive review.
/// </summary>
public static class ParityGate
{
    /// <summary>Global acceptance budget for planetary longitude parity, in arcseconds (AST-09 / FR-02 AC-5).</summary>
    public const double PlanetaryToleranceArcseconds = 1.0;

    /// <summary>Mean angular separation between two longitudes in arcseconds, always non-negative.</summary>
    public static double SeparationArcseconds(double first, double second)
    {
        var delta = ((first - second) % 360.0 + 540.0) % 360.0 - 180.0;
        return Math.Abs(delta) * 3600.0;
    }

    /// <summary>Evaluates one fixture against a computed longitude.</summary>
    public static ParityOutcome Evaluate(ParityFixture fixture, double computedTropicalLongitudeDegrees)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var separation = SeparationArcseconds(fixture.TropicalLongitudeDegrees, computedTropicalLongitudeDegrees);
        return new ParityOutcome(fixture, computedTropicalLongitudeDegrees, separation,
            separation <= fixture.ToleranceArcseconds);
    }
}

/// <summary>The result of one parity comparison.</summary>
public sealed record ParityOutcome(
    ParityFixture Fixture,
    double ComputedLongitudeDegrees,
    double SeparationArcseconds,
    bool Passed);
