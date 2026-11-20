using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Reference;

/// <summary>
/// Rotates a J2000 ecliptic vector into the ecliptic of date, and converts it to longitude and latitude.
/// </summary>
/// <remarks>
/// <para><b>Why this exists rather than a Horizons parameter.</b> Horizons offers exactly two reference planes for
/// vectors - <c>'ECLIPTIC'</c> (<i>Ecliptic of J2000.0</i>) and <c>'FRAME'</c> (<i>ICRF</i>). Verified against the
/// live API: **there is no ecliptic-of-date product**. The engine publishes longitudes against the mean equinox of
/// date, so the rotation has to happen here, and doing it here means it is the same code for every row rather than a
/// per-request setting someone can forget.</para>
/// <para><b>What the rotation is.</b> Precession in longitude only. This is deliberately minimal: the reference rows
/// are geometric, so nutation and aberration are not applied, and applying them here would make the rows a different
/// quantity from the one the engine produces. Precession is the whole of the J2000-to-date frame change for a
/// geometric ecliptic longitude.</para>
/// </remarks>
public static class EclipticOfDate
{
    private const double ArcsecondsPerDegree = 3600.0;

    /// <summary>
    /// General precession in longitude, in arcseconds, from J2000 to the given instant.
    /// </summary>
    /// <remarks>
    /// <para>Meeus, <i>Astronomical Algorithms</i>, eq. 21.5 (the precession of the ecliptic variant), reduced to its
    /// dominant term plus the J2000 offset:</para>
    /// <code>
    /// p = 5029.0966" t + 1.11113" t^2 - 0.000006" t^3
    /// </code>
    /// <para>where <c>t</c> is Julian centuries of TT from J2000. The linear term carries the effect; the quadratic term
    /// is included because at 2200 it is ~0.03 deg and would otherwise be a systematic error at the end of the
    /// supported span. The stated budget for this function is set by that omission, not by the series the rows test.</para>
    /// </remarks>
    public static double PrecessionArcseconds(double julianDayTt)
    {
        var t = (julianDayTt - 2451545.0) / 36525.0;
        return 5029.0966 * t + 1.11113 * t * t - 0.000006 * t * t * t;
    }

    /// <summary>
    /// Converts a J2000 ecliptic cartesian vector to longitude and latitude of date.
    /// </summary>
    /// <remarks>
    /// Precession in longitude is applied as an additive rotation on the ecliptic longitude; latitude is unchanged by
    /// precession to the precision this reference holds. That is a stated approximation, not a hidden one.
    /// </remarks>
    public static (double LongitudeDegrees, double LatitudeDegrees) ToLongitudeLatitude(
        (double X, double Y, double Z) j2000Vector, TtInstant instant)
    {
        var longitudeJ2000 = Math.Atan2(j2000Vector.Y, j2000Vector.X) * 180.0 / Math.PI;
        if (longitudeJ2000 < 0) longitudeJ2000 += 360.0;

        var latitude = Math.Atan2(j2000Vector.Z,
            Math.Sqrt(j2000Vector.X * j2000Vector.X + j2000Vector.Y * j2000Vector.Y)) * 180.0 / Math.PI;

        var longitudeOfDate = longitudeJ2000 + PrecessionArcseconds(instant.JulianDay) / ArcsecondsPerDegree;
        if (longitudeOfDate < 0) longitudeOfDate += 360.0;
        if (longitudeOfDate >= 360.0) longitudeOfDate -= 360.0;

        return (longitudeOfDate, latitude);
    }
}

