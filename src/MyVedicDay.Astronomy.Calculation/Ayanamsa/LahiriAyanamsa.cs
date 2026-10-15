
namespace MyVedicDay.Astronomy;

/// <summary>
/// Lahiri (Chitrapaksha) ayanamsa as Drik Panchang applies it. The Government of India definition is 23°15′00.658″
/// (true) at 1956-03-21 00:00 TT, i.e. 23.245524743° once nutation is removed, advanced by the IAU general precession
/// in longitude. Drik Panchang publishes a value 24.1″ larger (24.224241° at sunrise on 2025-10-20 in New Delhi), and
/// its Sankranti moments correspond to it, so that constant is added here: Sankrantis and sidereal positions then
/// agree with Drik Panchang instead of coming out about 10 minutes early.
/// This is the mean ayanamsa, paired with mean-equinox longitudes (the Swiss Ephemeris convention).
/// </summary>
public sealed class LahiriAyanamsa : IAyanamsa
{
    private const double ReferenceJulianDay = 2435553.5;
    private const double ReferenceAyanamsaDegrees = 23.245524743;

    /// <summary>Drik Panchang's Lahiri value minus the IAE 1956 definition, in degrees (24.14″).</summary>
    private const double DrikPanchangOffsetDegrees = 0.006706;

    private static readonly double ReferencePrecessionArcSeconds =
        GeneralPrecessionArcSeconds(JulianDay.CenturiesSinceJ2000(ReferenceJulianDay));

    public double GetDegrees(double julianDayUt)
    {
        var t = JulianDay.CenturiesSinceJ2000(julianDayUt);
        return ReferenceAyanamsaDegrees + DrikPanchangOffsetDegrees
            + ((GeneralPrecessionArcSeconds(t) - ReferencePrecessionArcSeconds) / 3600d);
    }

    private static double GeneralPrecessionArcSeconds(double t) =>
        (5029.0966 * t) + (1.11113 * t * t) - (0.000006 * t * t * t);
}
