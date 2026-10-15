namespace MyVedicDay.Astronomy;

/// <summary>
/// Pure angle helpers used by the astronomical algorithms. All angles are in degrees unless stated otherwise.
/// </summary>
public static class AngleMath
{
    private const double DegreesToRadians = Math.PI / 180d;
    private const double RadiansToDegrees = 180d / Math.PI;

    /// <summary>Normalizes an angle to the range [0, 360).</summary>
    public static double NormalizeDegrees(double degrees)
    {
        var result = degrees % 360d;
        if (result < 0)
        {
            result += 360d;
        }

        return result >= 360d ? 0d : result;
    }

    /// <summary>Normalizes an angle to the range [-180, 180).</summary>
    public static double NormalizeSignedDegrees(double degrees)
    {
        var result = NormalizeDegrees(degrees);
        return result >= 180d ? result - 360d : result;
    }

    public static double Sin(double degrees) => Math.Sin(degrees * DegreesToRadians);

    public static double Cos(double degrees) => Math.Cos(degrees * DegreesToRadians);

    public static double Tan(double degrees) => Math.Tan(degrees * DegreesToRadians);

    public static double Asin(double value) => Math.Asin(value) * RadiansToDegrees;

    public static double Acos(double value) => Math.Acos(value) * RadiansToDegrees;

    public static double Atan2(double y, double x) => Math.Atan2(y, x) * RadiansToDegrees;
}
