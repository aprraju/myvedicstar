namespace MyVedicDay.Astronomy;

/// <summary>Geocentric ecliptic position of the Moon, mean equinox of date (no nutation).</summary>
/// <param name="Longitude">Ecliptic longitude in degrees [0, 360).</param>
/// <param name="Latitude">Ecliptic latitude in degrees.</param>
/// <param name="DistanceKm">Distance between the centres of the Earth and the Moon in kilometres.</param>
internal readonly record struct LunarEclipticPosition(double Longitude, double Latitude, double DistanceKm);

/// <summary>
/// Geocentric lunar position from the truncated ELP-2000/82 series in Meeus, "Astronomical Algorithms", chapter 47.
/// Accuracy is about 10 arc-seconds in longitude and 4 in latitude, i.e. well under a minute of time for Tithi and
/// Nakshatra transitions and for moonrise.
/// </summary>
internal static class LunarPosition
{
    private const double EarthRadiusKm = 6378.14;

    // Meeus table 47.A: multiples of D, M, M', F, the sine coefficient of longitude and the cosine coefficient of
    // distance (units of 10^-6 degrees and 10^-3 km).
    private static readonly (int D, int M, int MPrime, int F, int Longitude, int Distance)[] LongitudeDistanceTerms =
    [
        (0, 0, 1, 0, 6288774, -20905355), (2, 0, -1, 0, 1274027, -3699111), (2, 0, 0, 0, 658314, -2955968),
        (0, 0, 2, 0, 213618, -569925), (0, 1, 0, 0, -185116, 48888), (0, 0, 0, 2, -114332, -3149),
        (2, 0, -2, 0, 58793, 246158), (2, -1, -1, 0, 57066, -152138), (2, 0, 1, 0, 53322, -170733),
        (2, -1, 0, 0, 45758, -204586), (0, 1, -1, 0, -40923, -129620), (1, 0, 0, 0, -34720, 108743),
        (0, 1, 1, 0, -30383, 104755), (2, 0, 0, -2, 15327, 10321), (0, 0, 1, 2, -12528, 0),
        (0, 0, 1, -2, 10980, 79661), (4, 0, -1, 0, 10675, -34782), (0, 0, 3, 0, 10034, -23210),
        (4, 0, -2, 0, 8548, -21636), (2, 1, -1, 0, -7888, 24208), (2, 1, 0, 0, -6766, 30824),
        (1, 0, -1, 0, -5163, -8379), (1, 1, 0, 0, 4987, -16675), (2, -1, 1, 0, 4036, -12831),
        (2, 0, 2, 0, 3994, -10445), (4, 0, 0, 0, 3861, -11650), (2, 0, -3, 0, 3665, 14403),
        (0, 1, -2, 0, -2689, -7003), (2, 0, -1, 2, -2602, 0), (2, -1, -2, 0, 2390, 10056),
        (1, 0, 1, 0, -2348, 6322), (2, -2, 0, 0, 2236, -9884), (0, 1, 2, 0, -2120, 5751),
        (0, 2, 0, 0, -2069, 0), (2, -2, -1, 0, 2048, -4950), (2, 0, 1, -2, -1773, 4130),
        (2, 0, 0, 2, -1595, 0), (4, -1, -1, 0, 1215, -3958), (0, 0, 2, 2, -1110, 0),
        (3, 0, -1, 0, -892, 3258), (2, 1, 1, 0, -810, 2616), (4, -1, -2, 0, 759, -1897),
        (0, 2, -1, 0, -713, -2117), (2, 2, -1, 0, -700, 2354), (2, 1, -2, 0, 691, 0),
        (2, -1, 0, -2, 596, 0), (4, 0, 1, 0, 549, -1423), (0, 0, 4, 0, 537, -1117),
        (4, -1, 0, 0, 520, -1571), (1, 0, -2, 0, -487, -1739), (2, 1, 0, -2, -399, 0),
        (0, 0, 2, -2, -381, -4421), (1, 1, 1, 0, 351, 0), (3, 0, -2, 0, -340, 0),
        (4, 0, -3, 0, 330, 0), (2, -1, 2, 0, 327, 0), (0, 2, 1, 0, -323, 1165),
        (1, 1, -1, 0, 299, 0), (2, 0, 3, 0, 294, 0), (2, 0, -1, -2, 0, 8752),
    ];

    // Meeus table 47.B: multiples of D, M, M', F and the sine coefficient of latitude (units of 10^-6 degrees).
    private static readonly (int D, int M, int MPrime, int F, int Latitude)[] LatitudeTerms =
    [
        (0, 0, 0, 1, 5128122), (0, 0, 1, 1, 280602), (0, 0, 1, -1, 277693), (2, 0, 0, -1, 173237),
        (2, 0, -1, 1, 55413), (2, 0, -1, -1, 46271), (2, 0, 0, 1, 32573), (0, 0, 2, 1, 17198),
        (2, 0, 1, -1, 9266), (0, 0, 2, -1, 8822), (2, -1, 0, -1, 8216), (2, 0, -2, -1, 4324),
        (2, 0, 1, 1, 4200), (2, 1, 0, -1, -3359), (2, -1, -1, 1, 2463), (2, -1, 0, 1, 2211),
        (2, -1, -1, -1, 2065), (0, 1, -1, -1, -1870), (4, 0, -1, -1, 1828), (0, 1, 0, 1, -1794),
        (0, 0, 0, 3, -1749), (0, 1, -1, 1, -1565), (1, 0, 0, 1, -1491), (0, 1, 1, 1, -1475),
        (0, 1, 1, -1, -1410), (0, 1, 0, -1, -1344), (1, 0, 0, -1, -1335), (0, 0, 3, 1, 1107),
        (4, 0, 0, -1, 1021), (4, 0, -1, 1, 833), (0, 0, 1, -3, 777), (4, 0, -2, 1, 671),
        (2, 0, 0, -3, 607), (2, 0, 2, -1, 596), (2, -1, 1, -1, 491), (2, 0, -2, 1, -451),
        (0, 0, 3, -1, 439), (2, 0, 2, 1, 422), (2, 0, -3, -1, 421), (2, 1, -1, 1, -366),
        (2, 1, 0, 1, -351), (4, 0, 0, 1, 331), (2, -1, 1, 1, 315), (2, -2, 0, -1, 302),
        (0, 0, 1, 3, -283), (2, 1, 1, -1, -229), (1, 1, 0, -1, 223), (1, 1, 0, 1, 223),
        (0, 1, -2, -1, -220), (2, 1, -1, -1, -220), (1, 0, 1, 1, -185), (2, -1, -2, -1, 181),
        (0, 1, 2, 1, -177), (4, 0, -2, -1, 176), (4, -1, -1, -1, 166), (1, 0, 1, -1, -164),
        (4, 0, 1, -1, 132), (1, 0, -1, -1, -119), (4, -1, 0, -1, 115), (2, -2, 0, 1, 107),
    ];

    /// <summary>
    /// Geocentric ecliptic longitude referred to the mean equinox of date (no nutation), in degrees.
    /// </summary>
    public static double MeanEquinoxLongitude(double julianEphemerisDay)
    {
        var arguments = Arguments.At(julianEphemerisDay);
        return Longitude(arguments);
    }

    /// <summary>Geocentric longitude, latitude and distance, mean equinox of date (no nutation).</summary>
    public static LunarEclipticPosition Position(double julianEphemerisDay)
    {
        var arguments = Arguments.At(julianEphemerisDay);
        return new LunarEclipticPosition(Longitude(arguments), Latitude(arguments), Distance(arguments));
    }

    /// <summary>Equatorial horizontal parallax in degrees for a geocentric distance in kilometres.</summary>
    public static double HorizontalParallax(double distanceKm) => AngleMath.Asin(EarthRadiusKm / distanceKm);

    private static double Longitude(in Arguments a)
    {
        var sum = 0d;
        foreach (var term in LongitudeDistanceTerms)
        {
            sum += term.Longitude * a.EccentricityFactor(term.M) * AngleMath.Sin(a.Argument(term.D, term.M, term.MPrime, term.F));
        }

        sum += (3958 * AngleMath.Sin(a.A1))
            + (1962 * AngleMath.Sin(a.MeanLongitude - a.LatitudeArgument))
            + (318 * AngleMath.Sin(a.A2));

        return AngleMath.NormalizeDegrees(a.MeanLongitude + (sum / 1_000_000d));
    }

    private static double Latitude(in Arguments a)
    {
        var sum = 0d;
        foreach (var term in LatitudeTerms)
        {
            sum += term.Latitude * a.EccentricityFactor(term.M) * AngleMath.Sin(a.Argument(term.D, term.M, term.MPrime, term.F));
        }

        sum += (-2235 * AngleMath.Sin(a.MeanLongitude))
            + (382 * AngleMath.Sin(a.A3))
            + (175 * AngleMath.Sin(a.A1 - a.LatitudeArgument))
            + (175 * AngleMath.Sin(a.A1 + a.LatitudeArgument))
            + (127 * AngleMath.Sin(a.MeanLongitude - a.MoonAnomaly))
            - (115 * AngleMath.Sin(a.MeanLongitude + a.MoonAnomaly));

        return sum / 1_000_000d;
    }

    private static double Distance(in Arguments a)
    {
        var sum = 0d;
        foreach (var term in LongitudeDistanceTerms)
        {
            sum += term.Distance * a.EccentricityFactor(term.M) * AngleMath.Cos(a.Argument(term.D, term.M, term.MPrime, term.F));
        }

        return 385000.56 + (sum / 1000d);
    }

    /// <summary>The fundamental arguments of Meeus chapter 47, in degrees.</summary>
    private readonly record struct Arguments(
        double MeanLongitude,
        double Elongation,
        double SunAnomaly,
        double MoonAnomaly,
        double LatitudeArgument,
        double A1,
        double A2,
        double A3,
        double Eccentricity)
    {
        public static Arguments At(double julianEphemerisDay)
        {
            var t = JulianDay.CenturiesSinceJ2000(julianEphemerisDay);
            var t2 = t * t;
            var t3 = t2 * t;
            var t4 = t3 * t;

            return new Arguments(
                AngleMath.NormalizeDegrees(218.3164477 + (481267.88123421 * t) - (0.0015786 * t2) + (t3 / 538841d) - (t4 / 65194000d)),
                AngleMath.NormalizeDegrees(297.8501921 + (445267.1114034 * t) - (0.0018819 * t2) + (t3 / 545868d) - (t4 / 113065000d)),
                AngleMath.NormalizeDegrees(357.5291092 + (35999.0502909 * t) - (0.0001536 * t2) + (t3 / 24490000d)),
                AngleMath.NormalizeDegrees(134.9633964 + (477198.8675055 * t) + (0.0087414 * t2) + (t3 / 69699d) - (t4 / 14712000d)),
                AngleMath.NormalizeDegrees(93.2720950 + (483202.0175233 * t) - (0.0036539 * t2) - (t3 / 3526000d) + (t4 / 863310000d)),
                AngleMath.NormalizeDegrees(119.75 + (131.849 * t)),
                AngleMath.NormalizeDegrees(53.09 + (479264.290 * t)),
                AngleMath.NormalizeDegrees(313.45 + (481266.484 * t)),
                1 - (0.002516 * t) - (0.0000074 * t2));
        }

        public double Argument(int d, int m, int mPrime, int f) =>
            (d * Elongation) + (m * SunAnomaly) + (mPrime * MoonAnomaly) + (f * LatitudeArgument);

        /// <summary>Terms involving the Sun's anomaly M are multiplied by E (or E² for 2M).</summary>
        public double EccentricityFactor(int m) => Math.Abs(m) switch
        {
            1 => Eccentricity,
            2 => Eccentricity * Eccentricity,
            _ => 1d,
        };
    }
}
