namespace MyVedicDay.Astronomy;

/// <summary>
/// Nutation and obliquity of the ecliptic (Meeus, "Astronomical Algorithms", chapter 22, 0.5" precision).
/// </summary>
internal static class Nutation
{
    /// <summary>Nutation in longitude (Δψ) in degrees.</summary>
    public static double InLongitude(double julianEphemerisDay)
    {
        var (omega, sunMean, moonMean) = GetArguments(julianEphemerisDay);
        var arcSeconds = (-17.20 * AngleMath.Sin(omega)) - (1.32 * AngleMath.Sin(2 * sunMean))
            - (0.23 * AngleMath.Sin(2 * moonMean)) + (0.21 * AngleMath.Sin(2 * omega));
        return arcSeconds / 3600d;
    }

    /// <summary>True obliquity of the ecliptic (ε0 + Δε) in degrees.</summary>
    public static double TrueObliquity(double julianEphemerisDay)
    {
        var t = JulianDay.CenturiesSinceJ2000(julianEphemerisDay);
        var meanObliquity = 23.439291111 - (((46.8150 * t) + (0.00059 * t * t) - (0.001813 * t * t * t)) / 3600d);

        var (omega, sunMean, moonMean) = GetArguments(julianEphemerisDay);
        var nutationArcSeconds = (9.20 * AngleMath.Cos(omega)) + (0.57 * AngleMath.Cos(2 * sunMean))
            + (0.10 * AngleMath.Cos(2 * moonMean)) - (0.09 * AngleMath.Cos(2 * omega));

        return meanObliquity + (nutationArcSeconds / 3600d);
    }

    private static (double Omega, double SunMean, double MoonMean) GetArguments(double julianEphemerisDay)
    {
        var t = JulianDay.CenturiesSinceJ2000(julianEphemerisDay);
        return (
            125.04452 - (1934.136261 * t),
            280.4665 + (36000.7698 * t),
            218.3165 + (481267.8813 * t));
    }
}
