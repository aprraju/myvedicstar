namespace MyVedicDay.Astronomy;

/// <summary>
/// Solar coordinates from the Earth's heliocentric VSOP87 series as truncated in Meeus, "Astronomical Algorithms",
/// chapter 32 and appendix III (chapter 25, "higher accuracy"). Accuracy is about 1″, i.e. a few seconds of Sankranti time.
/// </summary>
internal static class SolarPosition
{
    // Constant of aberration (κ) times the light-time factor, in arc-seconds at 1 AU.
    private const double AberrationArcSeconds = 20.4898;

    // FK5 correction to the VSOP87 longitude (Meeus 32.3), in arc-seconds.
    private const double Fk5CorrectionArcSeconds = -0.09033;

    // Meeus appendix III, Earth: (A, B, C) with term = A·cos(B + C·τ); A in units of 10^-8 radians (AU for R).
    private static readonly (double A, double B, double C)[][] LongitudeSeries =
    [
        [
            (175347046, 0, 0), (3341656, 4.6692568, 6283.07585), (34894, 4.6261, 12566.1517), (3497, 2.7441, 5753.3849),
            (3418, 2.8289, 3.5231), (3136, 3.6277, 77713.7715), (2676, 4.4181, 7860.4194), (2343, 6.1352, 3930.2097),
            (1324, 0.7425, 11506.7698), (1273, 2.0371, 529.691), (1199, 1.1096, 1577.3435), (990, 5.233, 5884.927),
            (902, 2.045, 26.298), (857, 3.508, 398.149), (780, 1.179, 5223.694), (753, 2.533, 5507.553),
            (505, 4.583, 18849.228), (492, 4.205, 775.523), (357, 2.92, 0.067), (317, 5.849, 11790.629),
            (284, 1.899, 796.298), (271, 0.315, 10977.079), (243, 0.345, 5486.778), (206, 4.806, 2544.314),
            (205, 1.869, 5573.143), (202, 2.458, 6069.777), (156, 0.833, 213.299), (132, 3.411, 2942.463),
            (126, 1.083, 20.775), (115, 0.645, 0.98), (103, 0.636, 4694.003), (102, 0.976, 15720.839),
            (102, 4.267, 7.114), (99, 6.21, 2146.17), (98, 0.68, 155.42), (86, 5.98, 161000.69),
            (85, 1.3, 6275.96), (85, 3.67, 71430.7), (80, 1.81, 17260.15), (79, 3.04, 12036.46),
            (75, 1.76, 5088.63), (74, 3.5, 3154.69), (74, 4.68, 801.82), (70, 0.83, 9437.76),
            (62, 3.98, 8827.39), (61, 1.82, 7084.9), (57, 2.78, 6286.6), (56, 4.39, 14143.5),
            (56, 3.47, 6279.55), (52, 0.19, 12139.55), (52, 1.33, 1748.02), (51, 0.28, 5856.48),
            (49, 0.49, 1194.45), (41, 5.37, 8429.24), (41, 2.4, 19651.05), (39, 6.17, 10447.39),
            (37, 6.04, 10213.29), (37, 2.57, 1059.38), (36, 1.71, 2352.87), (36, 1.78, 6812.77),
            (33, 0.59, 17789.85), (30, 0.44, 83996.85), (30, 2.74, 1349.87), (25, 3.16, 4690.48),
        ],
        [
            (628331966747, 0, 0), (206059, 2.678235, 6283.07585), (4303, 2.6351, 12566.1517), (425, 1.59, 3.523),
            (119, 5.796, 26.298), (109, 2.966, 1577.344), (93, 2.59, 18849.23), (72, 1.14, 529.69),
            (68, 1.87, 398.15), (67, 4.41, 5507.55), (59, 2.89, 5223.69), (56, 2.17, 155.42),
            (45, 0.4, 796.3), (36, 0.47, 775.52), (29, 2.65, 7.11), (21, 5.34, 0.98),
            (19, 1.85, 5486.78), (19, 4.97, 213.3), (17, 2.99, 6275.96), (16, 0.03, 2544.31),
            (16, 1.43, 2146.17), (15, 1.21, 10977.08), (12, 2.83, 1748.02), (12, 3.26, 5088.63),
            (12, 5.27, 1194.45), (12, 2.08, 4694), (11, 0.77, 553.57), (10, 1.3, 6286.6),
            (10, 4.24, 1349.87), (9, 2.7, 242.73), (9, 5.64, 951.72), (8, 5.3, 2352.87),
            (6, 2.65, 9437.76), (6, 4.67, 4690.48),
        ],
        [
            (52919, 0, 0), (8720, 1.0721, 6283.0758), (309, 0.867, 12566.152), (27, 0.05, 3.52),
            (16, 5.19, 26.3), (16, 3.68, 155.42), (10, 0.76, 18849.23), (9, 2.06, 77713.77),
            (7, 0.83, 775.52), (5, 4.66, 1577.34), (4, 1.03, 7.11), (4, 3.44, 5573.14),
            (3, 5.14, 796.3), (3, 6.05, 5507.55), (3, 1.19, 242.73), (3, 6.12, 529.69),
            (3, 0.31, 398.15), (3, 2.28, 553.57), (2, 4.38, 5223.69), (2, 3.75, 0.98),
        ],
        [
            (289, 5.844, 6283.076), (35, 0, 0), (17, 5.49, 12566.15), (3, 5.2, 155.42),
            (1, 4.72, 3.52), (1, 5.3, 18849.23), (1, 5.97, 242.73),
        ],
        [(114, 3.142, 0), (8, 4.13, 6283.08), (1, 3.84, 12566.15)],
        [(1, 3.14, 0)],
    ];

    // Radius vector, truncated to the terms that matter for the aberration (well under 0.01″).
    private static readonly (double A, double B, double C)[][] RadiusSeries =
    [
        [
            (100013989, 0, 0), (1670700, 3.0984635, 6283.07585), (13956, 3.05525, 12566.1517), (3084, 5.1985, 77713.7715),
            (1628, 1.1739, 5753.3849), (1576, 2.8469, 7860.4194), (925, 5.453, 11506.77), (542, 4.564, 3930.21),
        ],
        [(103019, 1.10749, 6283.07585), (1721, 1.0644, 12566.1517), (702, 3.142, 0)],
        [(4359, 5.7846, 6283.0758), (124, 5.579, 12566.152)],
    ];

    /// <summary>
    /// Geocentric longitude referred to the mean equinox of date, corrected for aberration but not nutation.
    /// </summary>
    public static double MeanEquinoxLongitude(double julianEphemerisDay)
    {
        var tau = (julianEphemerisDay - JulianDay.J2000) / 365250d;
        var heliocentricDegrees = Evaluate(LongitudeSeries, tau) * 180d / Math.PI;
        var radius = Evaluate(RadiusSeries, tau);

        // Geocentric Sun = heliocentric Earth + 180°, then FK5 and aberration.
        var geometric = heliocentricDegrees + 180d;
        var fk5 = Fk5CorrectionArcSeconds / 3600d;
        var aberration = -AberrationArcSeconds / radius / 3600d;

        return AngleMath.NormalizeDegrees(geometric + fk5 + aberration);
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

    /// <summary>Σ τⁿ·Σ A·cos(B + C·τ), scaled from 10^-8 units.</summary>
    private static double Evaluate((double A, double B, double C)[][] series, double tau)
    {
        var result = 0d;
        var power = 1d;
        foreach (var terms in series)
        {
            var sum = 0d;
            foreach (var (a, b, c) in terms)
            {
                sum += a * Math.Cos(b + (c * tau));
            }

            result += sum * power;
            power *= tau;
        }

        return result / 1e8;
    }
}
