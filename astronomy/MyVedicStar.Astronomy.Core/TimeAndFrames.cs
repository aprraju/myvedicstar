namespace MyVedicStar.Astronomy.Core;

/// <summary>
/// Julian Day in the Universal Time scale. Consumers pass this value; the engine performs its own UT to TT/TDB
/// conversion internally and reports the correction it applied in provenance, so an existing caller's Julian Day
/// never silently changes meaning.
/// </summary>
public readonly record struct JulianDayUt(double Value)
{
    /// <summary>The J2000.0 epoch, 2000-01-01 12:00 UT.</summary>
    public static readonly JulianDayUt J2000 = new(2451545.0);

    public bool IsFinite => double.IsFinite(Value);

    public override string ToString() => Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A right ascension and declination in degrees.</summary>
public readonly record struct EquatorialPoint(double RightAscensionDegrees, double DeclinationDegrees);

/// <summary>An ecliptic longitude and latitude in degrees, with an optional distance in astronomical units.</summary>
public readonly record struct EclipticPoint(double LongitudeDegrees, double LatitudeDegrees, double? DistanceAu);

/// <summary>An observer on the terrestrial surface. Longitude is positive east; elevation is metres above the
/// ellipsoid and is used only where a topocentric correction is requested.</summary>
public readonly record struct ObserverLocation(double LatitudeDegrees, double LongitudeDegrees, double ElevationMetres)
{
    public bool IsValid => LatitudeDegrees is >= -90 and <= 90 && LongitudeDegrees is >= -180 and <= 180;
}

/// <summary>The bodies this engine can compute. An identifier absent from the supported set is not supported; the
/// engine never substitutes a nearby body or an approximation (see <see cref="AstronomyCapabilities"/>).</summary>
public enum CelestialBody
{
    Sun, Moon, Mercury, Venus, Mars, Jupiter, Saturn, Uranus, Neptune, Pluto, MeanNode, TrueNode
}

/// <summary>Which node convention a caller wants. Both are supported and selectable; neither is a silent alias of
/// the other.</summary>
public enum NodeConvention { Mean, True }

/// <summary>Ayanamsa definitions. Lahiri is the product default; Raman and Krishnamurti are engine API surface and
/// are not enabled as a product default without a separate owner decision.</summary>
public enum AyanamsaKind { Lahiri, Raman, Krishnamurti }

/// <summary>Longitude convention requested of the engine.</summary>
public enum ZodiacConvention { Tropical, Sidereal }
