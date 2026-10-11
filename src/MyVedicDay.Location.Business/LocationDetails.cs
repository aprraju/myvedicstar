namespace MyVedicDay.Location;

/// <summary>How the coordinates of a location were obtained.</summary>
public enum LocationSource
{
    /// <summary>Coordinates supplied by the client, typically browser geolocation.</summary>
    Coordinates = 1,

    /// <summary>Coordinates looked up from a "City, State[, Country]" place name.</summary>
    Place,
}

/// <summary>
/// Resolved location. Coordinates are normalized to the configured precision, which is also the cache granularity.
/// </summary>
/// <param name="Latitude">Normalized latitude.</param>
/// <param name="Longitude">Normalized longitude.</param>
/// <param name="TimeZoneId">Authoritative IANA time zone.</param>
/// <param name="Source">Whether the coordinates came from the client or from a place search.</param>
/// <param name="Place">
/// The matched city for place searches, or the nearest city for coordinates; <see langword="null"/> when none is nearby.
/// </param>
public sealed record LocationDetails(double Latitude, double Longitude, string TimeZoneId, LocationSource Source, Place? Place);

/// <summary>A named city. Place data © GeoNames (CC BY 4.0); attribution must be shown wherever it is displayed.</summary>
public sealed record Place(string City, string? State, string Country, string CountryCode)
{
    /// <summary>"City, State, Country".</summary>
    public string DisplayName => string.Join(", ", new[] { City, State, Country }.Where(part => !string.IsNullOrEmpty(part)));
}
