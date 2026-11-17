using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Runs the primary (SPICE) provider and the analytical provider over the same input, returns the primary result and
/// reports the delta to the registered observers (FR-06 <c>compare</c> mode).
/// </summary>
/// <remarks>
/// This is what makes the parity gate possible in CI without a second calculation path: consumers still see exactly one
/// <see cref="IEphemeris"/>, and the comparison is an observation, not an alternative implementation.
/// </remarks>
internal sealed class ComparingEphemeris : IEphemeris
{
    private readonly IEphemeris _primary;
    private readonly IEphemeris _reference;
    private readonly IReadOnlyList<IParityObserver> _observers;

    public ComparingEphemeris(IEphemerisEngine engine, IEnumerable<IParityObserver> observers)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _primary = new AstronomyEngineEphemerisAdapter(engine);
        _reference = new MeeusEphemeris();
        _observers = observers?.ToArray() ?? [];
    }

    public double GetSunLongitude(double julianDayUt) =>
        Compare(CelestialBody.Sun, julianDayUt, _primary.GetSunLongitude, _reference.GetSunLongitude);

    public double GetMoonLongitude(double julianDayUt) =>
        Compare(CelestialBody.Moon, julianDayUt, _primary.GetMoonLongitude, _reference.GetMoonLongitude);

    public EquatorialCoordinates GetSunEquatorial(double julianDayUt) => _primary.GetSunEquatorial(julianDayUt);

    public LunarEquatorialCoordinates GetMoonEquatorial(double julianDayUt) =>
        _primary.GetMoonEquatorial(julianDayUt);

    private double Compare(CelestialBody body, double julianDayUt, Func<double, double> primary,
        Func<double, double> reference)
    {
        var primaryValue = primary(julianDayUt);
        var referenceValue = reference(julianDayUt);
        var separation = ParityGate.SeparationArcseconds(primaryValue, referenceValue);

        // Emit the delta so the parity gate can assert on it; the consumer still receives the primary result.
        foreach (var observer in _observers)
            observer.OnDelta(new ParityDelta(body, julianDayUt, primaryValue, referenceValue, separation,
                WithinTolerance: separation <= ParityGate.PlanetaryToleranceArcseconds));

        return primaryValue;
    }
}

/// <summary>Collects parity deltas for a test or a diagnostic run. Not a production sink.</summary>
public sealed class RecordingParityObserver : IParityObserver
{
    private readonly List<ParityDelta> _deltas = [];
    public IReadOnlyList<ParityDelta> Deltas => _deltas;
    public void OnDelta(ParityDelta delta) => _deltas.Add(delta);
}
