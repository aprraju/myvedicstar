namespace MyVedicStar.Astronomy.Coordinates;

/// <summary>
/// Time-scale conversions between the scales FR-03 names: UTC, UT1, TT and TDB.
/// </summary>
/// <remarks>
/// <para>Epoch and model versions, stated so a number is always attributable:</para>
/// <list type="bullet">
/// <item>Julian Date is the standard astronomical epoch: JD 2451545.0 = 2000-01-01 12:00 TT.</item>
/// <item>Delta T (TT - UT1) uses the Espenak-Meeus polynomial set (NASA Five Millennium Canon of Solar Eclipses),
/// the same model the existing Panchang calculation uses, so the two agree by construction.</item>
/// <item>Leap seconds follow the IERS Bulletin C table from 1972; before 1972 UTC did not exist as a uniform scale
/// and the engine treats UT1 and UTC as equal, which is stated here rather than implied.</item>
/// <item>TDB - TT uses the leading periodic term of the standard series (max about 1.7 ms), which is far below every
/// tolerance in AST-09.</item>
/// </list>
/// <para>These conversions are pure: no ambient clock, locale or timezone is read.</para>
/// </remarks>
public static class TimeScales
{
    private const double SecondsPerDay = 86400.0;

    /// <summary>Leap seconds introduced since 1972, as (UTC date effective, cumulative TAI-UTC seconds).
    /// Source: IERS Bulletin C; the table stops at the last announced leap second, because a future leap second
    /// cannot be predicted and inventing one would be a silent error.</summary>
    private static readonly (DateOnly Effective, int TaiMinusUtc)[] LeapSecondTable =
    [
        (new DateOnly(1972, 1, 1), 10),
        (new DateOnly(1972, 7, 1), 11),
        (new DateOnly(1973, 1, 1), 12),
        (new DateOnly(1974, 1, 1), 13),
        (new DateOnly(1975, 1, 1), 14),
        (new DateOnly(1976, 1, 1), 15),
        (new DateOnly(1977, 1, 1), 16),
        (new DateOnly(1978, 1, 1), 17),
        (new DateOnly(1979, 1, 1), 18),
        (new DateOnly(1980, 1, 1), 19),
        (new DateOnly(1981, 7, 1), 20),
        (new DateOnly(1982, 7, 1), 21),
        (new DateOnly(1983, 7, 1), 22),
        (new DateOnly(1985, 7, 1), 23),
        (new DateOnly(1988, 1, 1), 24),
        (new DateOnly(1990, 1, 1), 25),
        (new DateOnly(1991, 1, 1), 26),
        (new DateOnly(1992, 7, 1), 27),
        (new DateOnly(1993, 7, 1), 28),
        (new DateOnly(1994, 7, 1), 29),
        (new DateOnly(1996, 1, 1), 30),
        (new DateOnly(1997, 7, 1), 31),
        (new DateOnly(1999, 1, 1), 32),
        (new DateOnly(2006, 1, 1), 33),
        (new DateOnly(2009, 1, 1), 34),
        (new DateOnly(2012, 7, 1), 35),
        (new DateOnly(2015, 7, 1), 36),
        (new DateOnly(2017, 1, 1), 37)
    ];

    /// <summary>TT - TAI is exactly 32.184 s by definition of the Terrestrial Time scale.</summary>
    public const double TtMinusTaiSeconds = 32.184;

    /// <summary>The last date covered by the announced leap-second table. A date beyond it is reported, not
    /// guessed, because assuming no new leap second is an assumption and not a fact.</summary>
    /// <summary>The effective date of the most recently announced leap second; the table is authoritative up to
    /// and including it. Beyond this the engine reports "table does not cover this instant" rather than assuming
    /// no further leap second occurred.</summary>
    public static DateOnly LeapSecondTableValidTo { get; } = LeapSecondTable[^1].Effective;

    /// <summary>True when the leap-second table covers this instant, so a caller can tell whether a UTC to TAI
    /// conversion is authoritative or extrapolated.</summary>
    public static bool IsLeapSecondTableApplicable(double julianDayUt) =>
        JulianDayToUtc(julianDayUt) >= LeapSecondTable[0].Effective;

    /// <summary>TAI - UTC in seconds for an instant. Before 1972 this returns 0 and
    /// <see cref="IsLeapSecondTableApplicable"/> is false, which callers must treat as "UTC equals UT1 here".</summary>
    public static int TaiMinusUtcSeconds(double julianDayUt)
    {
        var date = JulianDayToUtc(julianDayUt);
        var value = 0;
        foreach (var (effective, taiMinusUtc) in LeapSecondTable)
        {
            if (date < effective) break;
            value = taiMinusUtc;
        }
        return value;
    }

    /// <summary>Delta T = TT - UT1 in seconds (Espenak-Meeus polynomial set).</summary>
    public static double DeltaTSeconds(double julianDayUt)
    {
        var year = 2000.0 + ((julianDayUt - 2451545.0) / 365.25);
        return year switch
        {
            // Espenak-Meeus: the same polynomial covers the whole span before the year 500.
            < 500 => 10583.6 + (-1014.41 * U(year, -500)) + (33.78311 * P(U(year, -500), 2))
                     + (-5.952053 * P(U(year, -500), 3)) + (-0.1798452 * P(U(year, -500), 4))
                     + (0.022174192 * P(U(year, -500), 5)) + (0.0090316521 * P(U(year, -500), 6)),
            < 1600 => 120.0 + (-0.9808 * U(year, 500)) + (-0.01532 * P(U(year, 500), 2))
                      + (P(U(year, 500), 3) / 7129.0),
            < 1700 => 120.0 - (0.9808 * U(year, 1600)) - (0.01532 * P(U(year, 1600), 2))
                      + (P(U(year, 1600), 3) / 7129.0),
            < 1800 => 8.83 + (0.1603 * U(year, 1700)) + (-0.0059285 * P(U(year, 1700), 2))
                      + (0.00013336 * P(U(year, 1700), 3)) - (P(U(year, 1700), 4) / 1174000.0),
            < 1860 => 13.72 + (-0.332447 * T(year, 1800)) + (0.0068612 * P(T(year, 1800), 2))
                      + (0.0041116 * P(T(year, 1800), 3)) + (-0.00037436 * P(T(year, 1800), 4))
                      + (0.0000121272 * P(T(year, 1800), 5)) + (-0.0000001699 * P(T(year, 1800), 6))
                      + (0.000000000875 * P(T(year, 1800), 7)),
            < 1900 => 7.62 + (0.5737 * T(year, 1860)) + (-0.251754 * P(T(year, 1860), 2))
                      + (0.01680668 * P(T(year, 1860), 3)) + (-0.0004473624 * P(T(year, 1860), 4))
                      + (P(T(year, 1860), 5) / 233174.0),
            // From 1860 onward the canonical polynomials take t = (year - start) in YEARS, not centuries.
            < 1920 => -2.79 + (1.494119 * T(year, 1900)) + (-0.0598939 * P(T(year, 1900), 2))
                      + (0.0061966 * P(T(year, 1900), 3)) + (-0.000197 * P(T(year, 1900), 4)),
            < 1941 => 21.20 + (0.84493 * T(year, 1920)) + (-0.076100 * P(T(year, 1920), 2))
                      + (0.0020936 * P(T(year, 1920), 3)),
            < 1961 => 29.07 + (0.407 * T(year, 1950)) + (-P(T(year, 1950), 2) / 233.0)
                      + (P(T(year, 1950), 3) / 2547.0),
            < 1986 => 45.45 + (1.067 * T(year, 1975)) + (-P(T(year, 1975), 2) / 260.0)
                      + (-P(T(year, 1975), 3) / 718.0),
            < 2005 => 63.86 + (0.3345 * T(year, 2000)) + (-0.060374 * P(T(year, 2000), 2))
                      + (0.0017275 * P(T(year, 2000), 3)) + (0.000651814 * P(T(year, 2000), 4))
                      + (0.00002373599 * P(T(year, 2000), 5)),
            < 2050 => 62.92 + (0.32217 * T(year, 2000)) + (0.005589 * P(T(year, 2000), 2)),
            < 2150 => -20.0 + (32.0 * Math.Pow((year - 1820.0) / 100.0, 2)) - (0.5628 * (2150.0 - year)),
            _ => -20.0 + (32.0 * Math.Pow((year - 1820.0) / 100.0, 2))
        };
    }

    /// <summary>Converts a Julian Day UT to Julian Day TT using Delta T.</summary>
    public static double UtToTt(double julianDayUt) => julianDayUt + (DeltaTSeconds(julianDayUt) / SecondsPerDay);

    /// <summary>Converts a Julian Day TT back to UT. Delta T is a function of the year, so the correction is
    /// evaluated at the UT instant; the residual is below 1 ms over any supported span.</summary>
    public static double TtToUt(double julianDayTt) =>
        julianDayTt - (DeltaTSeconds(julianDayTt) / SecondsPerDay);

    /// <summary>Julian Day UT to Julian Day TDB. TDB differs from TT by a periodic term of at most about 1.7 ms.</summary>
    public static double UtToTdb(double julianDayUt)
    {
        var tt = UtToTt(julianDayUt);
        return tt + (TdbMinusTtSeconds(tt) / SecondsPerDay);
    }

    /// <summary>TDB - TT in seconds, leading periodic term of the standard series.</summary>
    public static double TdbMinusTtSeconds(double julianDayTt)
    {
        var t = (julianDayTt - 2451545.0) / 36525.0;
        var g = DegreesToRadians(357.53 + (0.9856003 * (julianDayTt - 2451545.0)));
        return 0.001657 * Math.Sin(g) + (0.000022 * Math.Sin(2.0 * g));
    }

    /// <summary>UT1 - UTC in seconds. Modelled as zero: the engine does not consume IERS Earth-orientation data,
    /// and the difference is below 0.9 s, which is stated here rather than left implicit.</summary>
    public static double Ut1MinusUtcSeconds() => 0.0;

    private static DateOnly JulianDayToUtc(double julianDay)
    {
        // Standard JD to Gregorian calendar conversion.
        var jd = julianDay + 0.5;
        var z = (int)Math.Floor(jd);
        var f = jd - z;
        int a;
        if (z < 2299161) a = z;
        else
        {
            var alpha = (int)Math.Floor((z - 1867216.25) / 36524.25);
            a = z + 1 + alpha - (alpha / 4);
        }
        var b = a + 1524;
        var c = (int)Math.Floor((b - 122.1) / 365.25);
        var d = (int)Math.Floor(365.25 * c);
        var e = (int)Math.Floor((b - d) / 30.6001);
        var day = b - d - (int)Math.Floor(30.6001 * e);
        var month = e < 14 ? e - 1 : e - 13;
        var year = month > 2 ? c - 4716 : c - 4715;
        return new DateOnly(year, month, Math.Clamp(day, 1, DateTime.DaysInMonth(year, month)));
    }

    /// <summary>Espenak-Meeus century offset, used by the branches before 1800.</summary>
    private static double U(double year, double start) => (year - start) / 100.0;

    /// <summary>Espenak-Meeus YEAR offset, used by the branches from 1800 onward. Mixing this up with
    /// <see cref="U"/> silently produces a value that is authoritative-looking and wrong.</summary>
    private static double T(double year, double start) => year - start;
    private static double P(double value, int power) => Math.Pow(value, power);
    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180.0);
}
