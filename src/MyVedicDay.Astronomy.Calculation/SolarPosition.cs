namespace MyVedicDay.Astronomy;

/// <summary>
/// Solar coordinates (Meeus, "Astronomical Algorithms", chapter 25). Accuracy is about 0.01°.
/// </summary>
internal static class SolarPosition
{
    private const double AberrationDegrees = 0.00569;

    /// <summary>
    /// Geocentric longitude referred to the mean equinox of date, corrected for aberration but not nutation.
    /// </summary>
    public static double MeanEquinoxLongitude(double julianEphemerisDay)
    {
        var t = JulianDay.CenturiesSinceJ2000(julianEphemerisDay);
        var meanLongitude = 280.46646 + (36000.76983 * t) + (0.0003032 * t * t);
        var meanAnomaly = AngleMath.NormalizeDegrees(357.52911 + (35999.05029 * t) - (0.0001537 * t * t));
        var equationOfCenter = ((1.914602 - (0.004817 * t) - (0.000014 * t * t)) * AngleMath.Sin(meanAnomaly))
            + ((0.019993 - (0.000101 * t)) * AngleMath.Sin(2 * meanAnomaly))
            + (0.000289 * AngleMath.Sin(3 * meanAnomaly));

        return AngleMath.NormalizeDegrees(meanLongitude + equationOfCenter - AberrationDegrees);
    }

    /// <summary>
    /// Apparent right ascension and declination (true equinox of date).
    /// </summary>
    public static EquatorialCoordinates ApparentEquatorial(double julianEphemerisDay)
    {
        var longitude = MeanEquinoxLongitude(julianEphemerisDay) + Nutation.InLongitude(julianEphemerisDay);
        var obliquity = Nutation.TrueObliquity(julianEphemerisDay);

        var rightAscension = AngleMath.Atan2(AngleMath.Cos(obliquity) * AngleMath.Sin(longitude), AngleMath.Cos(longitude));
        var declination = AngleMath.Asin(AngleMath.Sin(obliquity) * AngleMath.Sin(longitude));

        return new EquatorialCoordinates(AngleMath.NormalizeDegrees(rightAscension), declination);
    }
}
