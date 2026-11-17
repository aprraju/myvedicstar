using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Coordinates;

/// <summary>Greenwich and local apparent sidereal time, used by event and house geometry.</summary>
public static class SiderealTime
{
    /// <summary>Greenwich mean sidereal time in degrees for a Julian Day UT (IAU 1982 polynomial,
    /// Vallado, Fundamentals of Astrodynamics, eq. 3-47).</summary>
    public static double GreenwichMeanSiderealTimeDegrees(JulianDayUt time)
    {
        var t = (time.Value - 2451545.0) / 36525.0;
        var gmst = 280.46061837 + 360.98564736629 * (time.Value - 2451545.0)
                   + 0.000387933 * t * t - (t * t * t) / 38710000.0;
        return AngleMath.Normalize(gmst);
    }

    /// <summary>Local mean sidereal time in degrees for an east-positive longitude.</summary>
    public static double LocalMeanSiderealTimeDegrees(JulianDayUt time, double longitudeDegrees) =>
        AngleMath.Normalize(GreenwichMeanSiderealTimeDegrees(time) + longitudeDegrees);
}
