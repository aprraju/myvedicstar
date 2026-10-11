namespace MyVedicDay.Astronomy;

/// <summary>
/// Greenwich mean sidereal time (Meeus, "Astronomical Algorithms", formula 12.4).
/// </summary>
internal static class SiderealTime
{
    public static double GreenwichMeanDegrees(double julianDayUt)
    {
        var t = JulianDay.CenturiesSinceJ2000(julianDayUt);
        var theta = 280.46061837 + (360.98564736629 * (julianDayUt - JulianDay.J2000))
            + (0.000387933 * t * t) - (t * t * t / 38710000d);
        return AngleMath.NormalizeDegrees(theta);
    }
}
