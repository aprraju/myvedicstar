using MyVedicStar.Astronomy.Coordinates;
using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Events;

/// <summary>
/// Geometric altitude of a body and the standard horizon definitions FR-04 needs.
/// </summary>
/// <remarks>
/// Standard refraction at the horizon is 34 arcminutes, applied by default (policy B1). Observer elevation, when
/// supplied, adds the dip of the horizon; when absent the caller is told the result assumes a sea-level horizon
/// rather than being silently given one.
/// </remarks>
public static class HorizonGeometry
{
    /// <summary>Standard atmospheric refraction at the horizon, in degrees.</summary>
    public const double StandardRefractionDegrees = 34.0 / 60.0;

    /// <summary>The geometric altitude at which the upper limb of the Sun is on the refracted horizon.</summary>
    public static double SunHorizonAltitudeDegrees(double? elevationMetres) =>
        -StandardRefractionDegrees - SolarSemiDiameterDegrees - DipDegrees(elevationMetres);

    /// <summary>The same for the Moon, whose semidiameter varies but is taken at its mean value here and stated as a
    /// documented assumption rather than silently varied.</summary>
    public static double MoonHorizonAltitudeDegrees(double? elevationMetres) =>
        -StandardRefractionDegrees - MoonSemiDiameterDegrees - DipDegrees(elevationMetres);

    /// <summary>Solar semidiameter at one astronomical unit, degrees.</summary>
    public const double SolarSemiDiameterDegrees = 16.0 / 60.0;

    /// <summary>Mean lunar semidiameter, degrees. The true value varies about 5% with distance; that variation is a
    /// documented assumption of this model, not a hidden one.</summary>
    public const double MoonSemiDiameterDegrees = 16.0 / 60.0;

    /// <summary>Dip of the horizon for an observer elevated above sea level, degrees.</summary>
    public static double DipDegrees(double? elevationMetres) =>
        elevationMetres is > 0 ? 1.76 * Math.Sqrt(elevationMetres.Value) / 60.0 : 0.0;

    /// <summary>
    /// Geometric (unrefracted) altitude in degrees from an equatorial position, the local mean sidereal time and the
    /// observer's latitude. This is real geometry and is exercised by tests; what AST-06 supplies is the equatorial
    /// position, not the altitude transform.
    /// </summary>
    public static double AltitudeDegrees(EquatorialPoint body, double localSiderealTimeDegrees, double observerLatitudeDegrees)
    {
        var hourAngle = AngleMath.ToRadians(AngleMath.Normalize(localSiderealTimeDegrees - body.RightAscensionDegrees));
        var declination = AngleMath.ToRadians(body.DeclinationDegrees);
        var latitude = AngleMath.ToRadians(observerLatitudeDegrees);

        var sinAltitude = (Math.Sin(latitude) * Math.Sin(declination))
                          + (Math.Cos(latitude) * Math.Cos(declination) * Math.Cos(hourAngle));
        return AngleMath.ToDegrees(Math.Asin(Math.Clamp(sinAltitude, -1.0, 1.0)));
    }

    /// <summary>Convenience overload that derives the local sidereal time from the instant and observer longitude.
    /// </summary>
    public static double AltitudeDegrees(EquatorialPoint body, JulianDayUt time, ObserverLocation observer) =>
        AltitudeDegrees(body, SiderealTime.LocalMeanSiderealTimeDegrees(time, observer.LongitudeDegrees),
            observer.LatitudeDegrees);
}
