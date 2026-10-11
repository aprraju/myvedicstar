namespace MyVedicDay.Astronomy;

/// <summary>
/// Apparent geocentric equatorial coordinates in degrees.
/// </summary>
public readonly record struct EquatorialCoordinates(double RightAscension, double Declination);

/// <summary>
/// Source of solar and lunar positions. Adapter boundary that allows a higher-precision ephemeris
/// (for example Swiss Ephemeris or JPL DE ephemerides) to replace the built-in analytical series.
/// </summary>
/// <remarks>All methods take a Julian Day in Universal Time; implementations handle ΔT internally.</remarks>
public interface IEphemeris
{
    /// <summary>Tropical solar longitude, mean equinox of date, in degrees.</summary>
    double GetSunLongitude(double julianDayUt);

    /// <summary>Tropical lunar longitude, mean equinox of date, in degrees.</summary>
    double GetMoonLongitude(double julianDayUt);

    /// <summary>Apparent solar right ascension and declination.</summary>
    EquatorialCoordinates GetSunEquatorial(double julianDayUt);
}
