using NodaTime;

namespace MyVedicDay.Location;

/// <summary>
/// Validated, normalized location with its authoritative time zone: the input every calculation domain needs.
/// </summary>
/// <param name="Latitude">Latitude rounded to <see cref="LocationOptions.CoordinateDecimalPlaces"/>.</param>
/// <param name="Longitude">Longitude rounded to <see cref="LocationOptions.CoordinateDecimalPlaces"/>.</param>
/// <param name="TimeZone">Time zone derived from the coordinates.</param>
/// <param name="Source">Whether the coordinates came from the client or from a place search.</param>
/// <param name="Place">The matched city, or the nearest city to the coordinates; <see langword="null"/> when none is nearby.</param>
public sealed record ResolvedLocation(
    double Latitude, double Longitude, DateTimeZone TimeZone, LocationSource Source = LocationSource.Coordinates, Place? Place = null)
{
    /// <summary>Contract form for API responses.</summary>
    public LocationDetails ToDetails() => new(Latitude, Longitude, TimeZone.Id, Source, Place);
}
