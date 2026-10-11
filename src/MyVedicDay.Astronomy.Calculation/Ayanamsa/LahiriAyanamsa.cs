
namespace MyVedicDay.Astronomy;

/// <summary>
/// Lahiri (Chitrapaksha) ayanamsa, the Government of India standard: 23°15′00.658″ (true) at 1956-03-21 00:00 TT,
/// i.e. 23.245524743° once nutation is removed, advanced by the IAU general precession in longitude.
/// This is the mean ayanamsa, paired with mean-equinox longitudes (the Swiss Ephemeris convention).
/// </summary>
public sealed class LahiriAyanamsa : IAyanamsa
{
    private const double ReferenceJulianDay = 2435553.5;
    private const double ReferenceAyanamsaDegrees = 23.245524743;

    private static readonly double ReferencePrecessionArcSeconds =
        GeneralPrecessionArcSeconds(JulianDay.CenturiesSinceJ2000(ReferenceJulianDay));

    public double GetDegrees(double julianDayUt)
    {
        var t = JulianDay.CenturiesSinceJ2000(julianDayUt);
        return ReferenceAyanamsaDegrees + ((GeneralPrecessionArcSeconds(t) - ReferencePrecessionArcSeconds) / 3600d);
    }

    private static double GeneralPrecessionArcSeconds(double t) =>
        (5029.0966 * t) + (1.11113 * t * t) - (0.000006 * t * t * t);
}
