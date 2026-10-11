namespace MyVedicDay.Astronomy;

/// <summary>
/// Geocentric lunar longitude from the truncated ELP-2000/82 series in Meeus, "Astronomical Algorithms", chapter 47.
/// Accuracy is about 10 arc-seconds, i.e. well under a minute of time for Tithi and Nakshatra transitions.
/// </summary>
internal static class LunarPosition
{
    // Meeus table 47.A: multiples of D, M, M', F and the sine coefficient in units of 10^-6 degrees.
    private static readonly (int D, int M, int MPrime, int F, int Coefficient)[] LongitudeTerms =
    [
        (0, 0, 1, 0, 6288774), (2, 0, -1, 0, 1274027), (2, 0, 0, 0, 658314), (0, 0, 2, 0, 213618),
        (0, 1, 0, 0, -185116), (0, 0, 0, 2, -114332), (2, 0, -2, 0, 58793), (2, -1, -1, 0, 57066),
        (2, 0, 1, 0, 53322), (2, -1, 0, 0, 45758), (0, 1, -1, 0, -40923), (1, 0, 0, 0, -34720),
        (0, 1, 1, 0, -30383), (2, 0, 0, -2, 15327), (0, 0, 1, 2, -12528), (0, 0, 1, -2, 10980),
        (4, 0, -1, 0, 10675), (0, 0, 3, 0, 10034), (4, 0, -2, 0, 8548), (2, 1, -1, 0, -7888),
        (2, 1, 0, 0, -6766), (1, 0, -1, 0, -5163), (1, 1, 0, 0, 4987), (2, -1, 1, 0, 4036),
        (2, 0, 2, 0, 3994), (4, 0, 0, 0, 3861), (2, 0, -3, 0, 3665), (0, 1, -2, 0, -2689),
        (2, 0, -1, 2, -2602), (2, -1, -2, 0, 2390), (1, 0, 1, 0, -2348), (2, -2, 0, 0, 2236),
        (0, 1, 2, 0, -2120), (0, 2, 0, 0, -2069), (2, -2, -1, 0, 2048), (2, 0, 1, -2, -1773),
        (2, 0, 0, 2, -1595), (4, -1, -1, 0, 1215), (0, 0, 2, 2, -1110), (3, 0, -1, 0, -892),
        (2, 1, 1, 0, -810), (4, -1, -2, 0, 759), (0, 2, -1, 0, -713), (2, 2, -1, 0, -700),
        (2, 1, -2, 0, 691), (2, -1, 0, -2, 596), (4, 0, 1, 0, 549), (0, 0, 4, 0, 537),
        (4, -1, 0, 0, 520), (1, 0, -2, 0, -487), (2, 1, 0, -2, -399), (0, 0, 2, -2, -381),
        (1, 1, 1, 0, 351), (3, 0, -2, 0, -340), (4, 0, -3, 0, 330), (2, -1, 2, 0, 327),
        (0, 2, 1, 0, -323), (1, 1, -1, 0, 299), (2, 0, 3, 0, 294),
    ];

    /// <summary>
    /// Geocentric ecliptic longitude referred to the mean equinox of date (no nutation), in degrees.
    /// </summary>
    public static double MeanEquinoxLongitude(double julianEphemerisDay)
    {
        var t = JulianDay.CenturiesSinceJ2000(julianEphemerisDay);
        var t2 = t * t;
        var t3 = t2 * t;
        var t4 = t3 * t;

        var meanLongitude = AngleMath.NormalizeDegrees(
            218.3164477 + (481267.88123421 * t) - (0.0015786 * t2) + (t3 / 538841d) - (t4 / 65194000d));
        var elongation = AngleMath.NormalizeDegrees(
            297.8501921 + (445267.1114034 * t) - (0.0018819 * t2) + (t3 / 545868d) - (t4 / 113065000d));
        var sunAnomaly = AngleMath.NormalizeDegrees(
            357.5291092 + (35999.0502909 * t) - (0.0001536 * t2) + (t3 / 24490000d));
        var moonAnomaly = AngleMath.NormalizeDegrees(
            134.9633964 + (477198.8675055 * t) + (0.0087414 * t2) + (t3 / 69699d) - (t4 / 14712000d));
        var latitudeArgument = AngleMath.NormalizeDegrees(
            93.2720950 + (483202.0175233 * t) - (0.0036539 * t2) - (t3 / 3526000d) + (t4 / 863310000d));
        var a1 = AngleMath.NormalizeDegrees(119.75 + (131.849 * t));
        var a2 = AngleMath.NormalizeDegrees(53.09 + (479264.290 * t));
        var eccentricity = 1 - (0.002516 * t) - (0.0000074 * t2);

        var sum = 0d;
        foreach (var term in LongitudeTerms)
        {
            var argument = (term.D * elongation) + (term.M * sunAnomaly) + (term.MPrime * moonAnomaly) + (term.F * latitudeArgument);
            var eccentricityFactor = Math.Abs(term.M) switch
            {
                1 => eccentricity,
                2 => eccentricity * eccentricity,
                _ => 1d,
            };

            sum += term.Coefficient * eccentricityFactor * AngleMath.Sin(argument);
        }

        sum += (3958 * AngleMath.Sin(a1))
            + (1962 * AngleMath.Sin(meanLongitude - latitudeArgument))
            + (318 * AngleMath.Sin(a2));

        return AngleMath.NormalizeDegrees(meanLongitude + (sum / 1_000_000d));
    }
}
