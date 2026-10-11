using MyVedicDay.Location;

namespace MyVedicDay.Api.Contracts.Locations;

/// <summary>
/// Maps Location module results to API response contracts.
/// </summary>
internal static class LocationResponseMapper
{
    public static LocationResponse ToResponse(this LocationDetails location) =>
        new(
            location.Latitude,
            location.Longitude,
            location.TimeZoneId,
            location.Source.ToString(),
            location.Place is { } place
                ? new PlaceResponse(place.City, place.State, place.Country, place.CountryCode, place.DisplayName)
                : null);
}
