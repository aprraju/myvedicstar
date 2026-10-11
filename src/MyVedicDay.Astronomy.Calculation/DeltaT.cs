namespace MyVedicDay.Astronomy;

/// <summary>
/// ΔT = TT − UT, using the Espenak–Meeus polynomial expressions (NASA Five Millennium Canon of Solar Eclipses).
/// </summary>
/// <remarks>
/// Within the supported range (1900–2100) the error is a few seconds of time, well inside the display precision
/// of Panchang periods. An ephemeris adapter backed by IERS data can replace this later.
/// </remarks>
internal static class DeltaT
{
    private const double SecondsPerDay = 86400d;

    public static double GetSeconds(double julianDayUt)
    {
        var year = 2000d + ((julianDayUt - JulianDay.J2000) / 365.25);

        if (year < 1900)
        {
            return LongTerm(year);
        }

        if (year < 1920)
        {
            var t = year - 1900;
            return -2.79 + (1.494119 * t) - (0.0598939 * t * t) + (0.0061966 * t * t * t) - (0.000197 * t * t * t * t);
        }

        if (year < 1941)
        {
            var t = year - 1920;
            return 21.20 + (0.84493 * t) - (0.076100 * t * t) + (0.0020936 * t * t * t);
        }

        if (year < 1961)
        {
            var t = year - 1950;
            return 29.07 + (0.407 * t) - (t * t / 233d) + (t * t * t / 2547d);
        }

        if (year < 1986)
        {
            var t = year - 1975;
            return 45.45 + (1.067 * t) - (t * t / 260d) - (t * t * t / 718d);
        }

        if (year < 2005)
        {
            var t = year - 2000;
            return 63.86 + (0.3345 * t) - (0.060374 * t * t) + (0.0017275 * t * t * t)
                + (0.000651814 * t * t * t * t) + (0.00002373599 * t * t * t * t * t);
        }

        if (year < 2050)
        {
            var t = year - 2000;
            return 62.92 + (0.32217 * t) + (0.005589 * t * t);
        }

        if (year < 2150)
        {
            return LongTerm(year) - (0.5628 * (2150 - year));
        }

        return LongTerm(year);
    }

    /// <summary>Converts a Julian Day in Universal Time to a Julian Ephemeris Day (Terrestrial Time).</summary>
    public static double ToJulianEphemerisDay(double julianDayUt) => julianDayUt + (GetSeconds(julianDayUt) / SecondsPerDay);

    private static double LongTerm(double year)
    {
        var u = (year - 1820) / 100;
        return -20 + (32 * u * u);
    }
}
