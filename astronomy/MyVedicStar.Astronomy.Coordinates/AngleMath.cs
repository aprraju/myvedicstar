namespace MyVedicStar.Astronomy.Coordinates;

/// <summary>Angle helpers. All public methods are total functions of their arguments: no ambient state, so callers
/// get identical output for identical input on any machine or locale (AC-7).</summary>
public static class AngleMath
{
    private const double FullTurn = 360.0;

    /// <summary>Normalises any angle into [0, 360).</summary>
    public static double Normalize(double degrees)
    {
        if (!double.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees), "Angle must be finite.");
        var value = degrees % FullTurn;
        return value < 0 ? value + FullTurn : value;
    }

    /// <summary>Signed shortest difference a - b, in (-180, 180].</summary>
    public static double Difference(double a, double b)
    {
        var difference = Normalize(a - b);
        return difference > 180.0 ? difference - FullTurn : difference;
    }

    public static double ToRadians(double degrees) => degrees * (Math.PI / 180.0);
    public static double ToDegrees(double radians) => radians * (180.0 / Math.PI);
}
