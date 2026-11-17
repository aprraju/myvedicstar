using MyVedicStar.Astronomy.Coordinates;
using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Houses;

/// <summary>
/// Ascendant and house geometry. House systems beyond the sidereal ascendant are deliberately not implemented yet:
/// the engine refuses an unsupported system rather than returning an untested cusp set.
/// </summary>
public static class HouseGeometry
{
    /// <summary>
    /// Tropical ascendant longitude in degrees for an instant and observer, using the standard
    /// atan2 formulation with the obliquity of date supplied by the caller.
    /// </summary>
    public static double AscendantLongitude(JulianDayUt time, ObserverLocation observer, double obliquityDegrees)
    {
        if (!observer.IsValid) throw new ArgumentOutOfRangeException(nameof(observer), "Observer coordinates are invalid.");

        var ramc = AngleMath.ToRadians(
            SiderealTime.LocalMeanSiderealTimeDegrees(time, observer.LongitudeDegrees));
        var epsilon = AngleMath.ToRadians(obliquityDegrees);
        var latitude = AngleMath.ToRadians(observer.LatitudeDegrees);

        var y = Math.Cos(ramc);
        var x = -(Math.Sin(ramc) * Math.Cos(epsilon) + Math.Tan(latitude) * Math.Sin(epsilon));
        return AngleMath.Normalize(AngleMath.ToDegrees(Math.Atan2(y, x)));
    }
}
