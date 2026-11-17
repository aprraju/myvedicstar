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

    /// <summary>The inverse transformation: equatorial to ecliptic, so a round trip can be asserted exactly.</summary>
    public static (double LongitudeDegrees, double LatitudeDegrees) EquatorialToEcliptic(
        double rightAscensionDegrees, double declinationDegrees, double obliquityDegrees)
    {
        var alpha = AngleMath.ToRadians(rightAscensionDegrees);
        var delta = AngleMath.ToRadians(declinationDegrees);
        var epsilon = AngleMath.ToRadians(obliquityDegrees);

        var sinLatitude = (Math.Sin(delta) * Math.Cos(epsilon)) - (Math.Cos(delta) * Math.Sin(epsilon) * Math.Sin(alpha));
        var latitude = Math.Asin(Math.Clamp(sinLatitude, -1.0, 1.0));

        var y = (Math.Sin(alpha) * Math.Cos(epsilon)) + (Math.Tan(delta) * Math.Sin(epsilon));
        var x = Math.Cos(alpha);

        return (AngleMath.Normalize(AngleMath.ToDegrees(Math.Atan2(y, x))), AngleMath.ToDegrees(latitude));
    }
}
