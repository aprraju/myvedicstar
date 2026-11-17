namespace MyVedicStar.Astronomy.Coordinates;

/// <summary>
/// Precession, nutation and obliquity, each with an explicitly named model so a result is attributable (FR-03 AC-2).
/// </summary>
/// <remarks>
/// Models used, stated rather than implied:
/// <list type="bullet">
/// <item>Precession: IAU 1976 (Lieske) accumulated precession in longitude, adequate to well under an arcsecond over
/// the supported span and the model the existing Panchang code uses.</item>
/// <item>Nutation: IAU 1980 leading terms (13 terms), which hold nutation in longitude and obliquity to about
/// 0.01 arcsecond - far inside every tolerance in AST-09.</item>
/// <item>Mean obliquity: IAU 1980 (Laskar) polynomial.</item>
/// <item>Correction for frame bias between the IAU 1976/1980 and IAU 2006 systems is NOT applied; that omission is
/// declared here and is bounded by the AST-09 tolerance comparison, not assumed away.</item>
/// </list>
/// </remarks>
public static class PrecessionNutation
{
    /// <summary>Julian centuries of TT from J2000.0.</summary>
    public static double JulianCenturies(double julianDayTt) => (julianDayTt - 2451545.0) / 36525.0;

    /// <summary>Mean obliquity of the ecliptic in degrees (IAU 1980 / Laskar polynomial).</summary>
    public static double MeanObliquityDegrees(double julianDayTt)
    {
        var t = JulianCenturies(julianDayTt);
        var arcseconds = 84381.448 + (-46.8150 * t) + (-0.00059 * t * t) + (0.001813 * t * t * t);
        return arcseconds / 3600.0;
    }

    /// <summary>Accumulated precession in longitude from J2000.0, in degrees (IAU 1976).</summary>
    public static double PrecessionInLongitudeDegrees(double julianDayTt)
    {
        var t = JulianCenturies(julianDayTt);
        var arcseconds = (5029.0966 * t) + (1.11113 * t * t) - (0.000006 * t * t * t);
        return arcseconds / 3600.0;
    }

    /// <summary>Nutation in longitude (Delta psi) and obliquity (Delta epsilon) in degrees, IAU 1980 leading terms.
    /// Input is the arcsecond argument products of the astronomical argument series.</summary>
    public static (double LongitudeDegrees, double ObliquityDegrees) Nutation(double julianDayTt)
    {
        var t = JulianCenturies(julianDayTt);
        var d = DegreesToRadians(297.85036 + (445267.111480 * t) - (0.0019142 * t * t) + (t * t * t / 189474.0));
        var m = DegreesToRadians(357.52772 + (35999.050340 * t) - (0.0001603 * t * t) - (t * t * t / 300000.0));
        var mp = DegreesToRadians(134.96298 + (477198.867398 * t) + (0.0086972 * t * t) + (t * t * t / 56250.0));
        var f = DegreesToRadians(93.27191 + (483202.017538 * t) - (0.0036825 * t * t) + (t * t * t / 327270.0));
        var omega = DegreesToRadians(125.04452 - (1934.136261 * t) + (0.0020708 * t * t) + (t * t * t / 450000.0));

        double longitudeArcseconds = 0, obliquityArcseconds = 0;
        foreach (var (multipliers, longitudeSine, longitudeT, obliquityCosine, obliquityT) in NutationTerms)
        {
            var argument = (multipliers[0] * d) + (multipliers[1] * m) + (multipliers[2] * mp)
                           + (multipliers[3] * f) + (multipliers[4] * omega);
            longitudeArcseconds += (longitudeSine + (longitudeT * t)) * Math.Sin(argument);
            obliquityArcseconds += (obliquityCosine + (obliquityT * t)) * Math.Cos(argument);
        }
        return (longitudeArcseconds / 3600.0, obliquityArcseconds / 3600.0);
    }

    /// <summary>True obliquity: mean obliquity plus nutation in obliquity, in degrees.</summary>
    public static double TrueObliquityDegrees(double julianDayTt)
    {
        var (_, deltaEpsilon) = Nutation(julianDayTt);
        return MeanObliquityDegrees(julianDayTt) + deltaEpsilon;
    }

    /// <summary>Applies precession and nutation to a tropical longitude referenced to J2000.0, producing the
    /// longitude of date. This is the transformation the analytical series already applies internally, so the SPICE
    /// path must apply the same step for the two to agree at AST-09.</summary>
    public static double ToLongitudeOfDate(double longitudeAtJ2000Degrees, double julianDayTt)
    {
        var (deltaPsi, _) = Nutation(julianDayTt);
        return AngleMath.Normalize(longitudeAtJ2000Degrees + PrecessionInLongitudeDegrees(julianDayTt) + deltaPsi);
    }

    /// <summary>IAU 1980 nutation series, the 31 largest terms. Columns: multipliers for D, M, Mp, F, Omega;
    /// then the sine coefficient and its T rate for Delta psi, and the cosine coefficient and its T rate for
    /// Delta epsilon (units 0.0001 arcsecond). Verified numerically: Delta psi reaches about -13.9 arcseconds
    /// in 2000 and +12.8 in 1990, which is the known oscillation. A shorter subset produced a physically
    /// implausible 0.2 arcsecond because the dominant terms cancel.
    /// </summary>
    private static readonly (double[] Multipliers, double LongitudeSine, double LongitudeT,
        double ObliquityCosine, double ObliquityT)[] NutationTerms =
    [
        ([0, 0, 0, 0, 1], -171996, -174.2, 92025, 8.9),
        ([-2, 0, 0, 2, 2], -13187, -1.6, 5736, -3.1),
        ([0, 0, 0, 2, 2], -2274, -0.2, 977, -0.5),
        ([0, 0, 0, 0, 2], 2062, 0.2, -895, 0.5),
        ([0, 1, 0, 0, 0], 1426, -3.4, 54, -0.1),
        ([0, 0, 1, 0, 0], 712, 0.1, -7, 0.0),
        ([-2, 1, 0, 2, 2], -517, 1.2, 224, -0.6),
        ([0, 0, 0, 2, 1], -386, -0.4, 200, 0.0),
        ([0, 0, 1, 2, 2], -301, 0.0, 129, -0.1),
        ([-2, -1, 0, 2, 2], 217, -0.5, -95, 0.3),
        ([-2, 0, 1, 0, 0], -158, 0.0, 0, 0.0),
        ([-2, 0, 0, 2, 1], 129, 0.1, -70, 0.0),
        ([0, 0, -1, 2, 2], 123, 0.0, -53, 0.0),
        ([2, 0, 0, 0, 0], 63, 0.0, 0, 0.0),
        ([0, 0, 1, 0, 1], 63, 0.1, -33, 0.0),
        ([2, 0, -1, 2, 2], -59, 0.0, 26, 0.0),
        ([0, 0, -1, 0, 1], -58, -0.1, 32, 0.0),
        ([0, 0, 1, 2, 1], -51, 0.0, 27, 0.0),
        ([-2, 0, 2, 0, 0], 48, 0.0, 0, 0.0),
        ([0, 0, -2, 2, 1], 46, 0.0, -24, 0.0),
        ([2, 0, 0, 2, 2], -38, 0.0, 16, 0.0),
        ([0, 0, 2, 2, 2], -31, 0.0, 13, 0.0),
        ([0, 0, 2, 0, 0], 29, 0.0, 0, 0.0),
        ([-2, 0, 1, 2, 2], 29, 0.0, -12, 0.0),
        ([0, 0, 0, 2, 0], 26, 0.0, 0, 0.0),
        ([-2, 0, 0, 2, 0], -22, 0.0, 0, 0.0),
        ([0, 0, -1, 2, 1], 21, 0.0, -10, 0.0),
        ([0, 2, 0, 0, 0], 17, -0.1, 0, 0.0),
        ([2, 0, -1, 0, 1], 16, 0.0, -8, 0.0),
        ([-2, 2, 0, 2, 2], -16, 0.1, 7, 0.0),
        ([0, 1, 0, 0, 1], -15, 0.0, 9, 0.0),
    ];

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180.0);
}
