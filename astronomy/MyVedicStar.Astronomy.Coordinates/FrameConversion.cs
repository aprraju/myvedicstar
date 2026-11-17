using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Coordinates;

/// <summary>Ecliptic and equatorial frame conversions. Inputs and outputs are degrees; no time-scale conversion is
/// performed here, so the caller controls which instant it is converting.</summary>
public static class FrameConversion
{
    public static EquatorialPoint EclipticToEquatorial(double longitudeDegrees, double latitudeDegrees,
        double obliquityDegrees)
    {
        var lambda = AngleMath.ToRadians(longitudeDegrees);
        var beta = AngleMath.ToRadians(latitudeDegrees);
        var epsilon = AngleMath.ToRadians(obliquityDegrees);

        var sinDeclination = Math.Sin(beta) * Math.Cos(epsilon) + Math.Cos(beta) * Math.Sin(epsilon) * Math.Sin(lambda);
        var declination = Math.Asin(Math.Clamp(sinDeclination, -1.0, 1.0));

        var y = Math.Sin(lambda) * Math.Cos(epsilon) - Math.Tan(beta) * Math.Sin(epsilon);
        var x = Math.Cos(lambda);

        return new EquatorialPoint(AngleMath.Normalize(AngleMath.ToDegrees(Math.Atan2(y, x))),
            AngleMath.ToDegrees(declination));
    }
}
