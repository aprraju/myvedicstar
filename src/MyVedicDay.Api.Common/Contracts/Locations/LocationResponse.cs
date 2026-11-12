namespace MyVedicDay.Api.Contracts.Locations;

/// <summary>Resolved location.</summary>
/// <param name="Latitude">Normalized latitude.</param>
/// <param name="Longitude">Normalized longitude.</param>
/// <param name="TimeZone">IANA time zone used for all local times.</param>
/// <param name="Source"><c>Coordinates</c> (browser location) or <c>Place</c> (city search).</param>
/// <param name="Place">Matched city, or the nearest city to the coordinates; null when none is within 50 km.</param>
public sealed record LocationResponse(double Latitude, double Longitude, string TimeZone, string Source, PlaceResponse? Place);

/// <summary>
/// A named city. Place data © GeoNames (https://www.geonames.org), CC BY 4.0: clients must show this attribution.
/// </summary>
/// <param name="City">City name.</param>
/// <param name="State">State or province, when known.</param>
/// <param name="Country">Country name.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2 code.</param>
/// <param name="DisplayName">"City, State, Country".</param>
public sealed record PlaceResponse(string City, string? State, string Country, string CountryCode, string DisplayName);
