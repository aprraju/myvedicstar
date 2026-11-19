using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Core;

/// <summary>
/// Which published claim a fixture row measures. The same body at the same instant can carry more than one published
/// tolerance, and they are not interchangeable (api#130).
/// </summary>
public enum FixtureClaim
{
    /// <summary>The composed engine value: Meeus 47.a shifted by the documented Drik Panchang lunar offset. This is what
    /// <c>IEphemeris.GetMoonLongitude</c> returns, so it is the claim the parity gate compares against.</summary>
    EngineReference = 0,

    /// <summary>The unoffset analytical series alone, at the worked example's own published precision. Deliberately has
    /// NO position of its own: it is a different claim about the same instant, not a second measurement of the engine
    /// value, and giving it a position would let the parity gate compare against a value the engine never returns.</summary>
    UnoffsetSeries = 1,
}

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
/// <param name="Claim">Which published claim this row measures. REQUIRED, with no default: a row that omits it would
/// silently join the analytic-vs-SPICE comparison set as <see cref="FixtureClaim.EngineReference"/>, and an
/// UnoffsetSeries row given a position would let the parity gate compare against a value the engine never returns.
/// Omission must fail to compile rather than mean something (api#136).</param>
public sealed record ParityFixture(
    CelestialBody Body,
    double JulianDayUt,
    double TropicalLongitudeDegrees,
    string Source,
    double ToleranceArcseconds,
    FixtureClaim Claim);

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
