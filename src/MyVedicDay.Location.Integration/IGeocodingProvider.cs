namespace MyVedicDay.Location.Integration;

/// <summary>
/// Converts place names to coordinates and coordinates to the nearest named place.
/// </summary>
public interface IGeocodingProvider
{
    /// <summary>
    /// Finds cities matching the search, best match first. Primary-name matches rank above alternate-name
    /// matches, then larger populations rank first.
    /// </summary>
    /// <exception cref="MyVedicDay.Shared.Exceptions.IntegrationException">The provider failed.</exception>
    ValueTask<IReadOnlyList<GeocodedPlace>> SearchAsync(PlaceSearch search, int maxResults, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the nearest city within <paramref name="maxDistanceKm"/>, or <see langword="null"/> if there is none.
    /// </summary>
    /// <exception cref="MyVedicDay.Shared.Exceptions.IntegrationException">The provider failed.</exception>
    ValueTask<GeocodedPlace?> FindNearestAsync(double latitude, double longitude, double maxDistanceKm, CancellationToken cancellationToken);
}

/// <summary>
/// A city search. <paramref name="State"/> matches a state/province name (or code, e.g. "TX");
/// <paramref name="Country"/> matches a country name, ISO 3166 alpha-2 or alpha-3 code, or a common alias ("USA", "UK").
/// </summary>
public sealed record PlaceSearch(string City, string? State = null, string? Country = null);

/// <summary>
/// A geocoded city.
/// </summary>
public sealed record GeocodedPlace(
    string City,
    string? State,
    string Country,
    string CountryCode,
    double Latitude,
    double Longitude,
    string? TimeZoneId);
