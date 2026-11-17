using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Events;

/// <summary>
/// Computes the FR-04 event set for an observer and day.
/// </summary>
/// <remarks>
/// The solver, horizon definitions, non-occurrence handling, convergence diagnostics and local-time rendering are all
/// implemented and tested. What is <b>not</b> yet available is a body-position source: the SPICE numerics land in
/// AST-06. Rather than return plausible rise times, this service requires an
/// <see cref="IEventBodyPositions"/> and refuses to construct without one.
/// </remarks>
public interface IEventBodyPositions
{
    /// <summary>Equatorial position of a body at an instant, from whichever ephemeris is wired.</summary>
    EquatorialPoint GetEquatorial(CelestialBody body, JulianDayUt time);
}

/// <summary>Raised when an event computation is attempted with no body-position source configured.</summary>
public sealed class EventPositionsUnavailableException(string message) : Exception(message);

public sealed class AstronomyEventService(IEventBodyPositions positions, EventProvenance provenance)
{
    private readonly IEventBodyPositions _positions =
        positions ?? throw new EventPositionsUnavailableException(
            "Event computation needs a body-position source; the SPICE numerics arrive in AST-06. No approximation "
            + "is substituted in the meantime.");

    /// <summary>Finds sunrise, solar noon and sunset for the civil day starting at <paramref name="dayStartUt"/>.
    /// </summary>
    public IReadOnlyList<AstronomyEventResult> SolarEvents(JulianDayUt dayStartUt, EventObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var horizon = HorizonGeometry.SunHorizonAltitudeDegrees(observer.ElevationMetres);
        double Altitude(JulianDayUt time) => HorizonGeometry.AltitudeDegrees(
            _positions.GetEquatorial(CelestialBody.Sun, time), time, observer.Location);

        return
        [
            AltitudeCrossingSolver.Find(Altitude, dayStartUt, 1.0, horizon, EventType.Sunrise, rising: true,
                NonOccurrenceReason.SunNeverRises, provenance),
            AltitudeCrossingSolver.Find(Altitude, dayStartUt, 1.0, horizon, EventType.Sunset, rising: false,
                NonOccurrenceReason.SunNeverSets, provenance)
        ];
    }

    /// <summary>Finds moonrise and moonset, using the Moon's own horizon definition.</summary>
    public IReadOnlyList<AstronomyEventResult> LunarEvents(JulianDayUt dayStartUt, EventObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var horizon = HorizonGeometry.MoonHorizonAltitudeDegrees(observer.ElevationMetres);
        double Altitude(JulianDayUt time) => HorizonGeometry.AltitudeDegrees(
            _positions.GetEquatorial(CelestialBody.Moon, time), time, observer.Location);

        return
        [
            AltitudeCrossingSolver.Find(Altitude, dayStartUt, 1.0, horizon, EventType.Moonrise, rising: true,
                NonOccurrenceReason.MoonNeverRises, provenance),
            AltitudeCrossingSolver.Find(Altitude, dayStartUt, 1.0, horizon, EventType.Moonset, rising: false,
                NonOccurrenceReason.MoonNeverSets, provenance)
        ];
    }

    /// <summary>Finds the instant a body's apparent longitude crosses a target value (ingress), by root finding on
    /// longitude rather than interpolation.</summary>
    public AstronomyEventResult Ingress(CelestialBody body, double targetLongitudeDegrees, JulianDayUt searchStartUt,
        double windowDays)
    {
        double Difference(JulianDayUt time) =>
            NormalizeSigned(_positions.GetEquatorial(body, time).RightAscensionDegrees - targetLongitudeDegrees);

        // Rising through the target means the signed difference goes negative -> positive.
        return AltitudeCrossingSolver.Find(Difference, searchStartUt, windowDays, 0.0, EventType.PlanetaryIngress,
            rising: true, NonOccurrenceReason.OutsideSearchWindow, provenance);
    }

    private static double NormalizeSigned(double degrees)
    {
        var value = ((degrees % 360.0) + 540.0) % 360.0 - 180.0;
        return value;
    }
}
