using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVedicDay.Location.Integration;
using MyVedicDay.Shared.Exceptions;
using NodaTime;

namespace MyVedicDay.Location;

/// <summary>
/// Turns a <see cref="LocationQuery"/> into validated, normalized coordinates with an authoritative time zone.
/// A place name, when given, determines the coordinates; otherwise the supplied coordinates are used.
/// </summary>
internal sealed partial class LocationResolver(
    PlaceResolver placeResolver,
    ITimeZoneLookupProvider timeZoneLookup,
    IDateTimeZoneProvider timeZones,
    IOptions<LocationOptions> options,
    ILogger<LocationResolver> logger) : ILocationResolver
{
    private const int MaxPlaceLength = 120;

    public async Task<ResolvedLocation> ResolveAsync(LocationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var coordinates = ValidateCoordinates(query.Latitude, query.Longitude);

        double latitude;
        double longitude;
        LocationSource source;
        Place? place;

        if (!string.IsNullOrWhiteSpace(query.Place))
        {
            if (query.Place.Length > MaxPlaceLength)
            {
                throw new ValidationException("place", $"Place must be at most {MaxPlaceLength} characters.");
            }

            var match = await placeResolver.FindPlaceAsync(query.Place, coordinates, cancellationToken);
            (latitude, longitude, source, place) = (match.Latitude, match.Longitude, LocationSource.Place, PlaceResolver.ToPlace(match));
        }
        else if (coordinates is { } supplied)
        {
            (latitude, longitude, source) = (supplied.Latitude, supplied.Longitude, LocationSource.Coordinates);
            place = await placeResolver.FindNearestCityAsync(latitude, longitude, cancellationToken);
        }
        else
        {
            throw new ValidationException(
                "location", "Provide latitude and longitude, or a place as \"City, State\" or \"City, State, Country\".");
        }

        var decimals = options.Value.CoordinateDecimalPlaces;
        var normalizedLatitude = Math.Round(latitude, decimals, MidpointRounding.AwayFromZero);
        var normalizedLongitude = Math.Round(longitude, decimals, MidpointRounding.AwayFromZero);

        var timeZone = await ResolveTimeZoneAsync(normalizedLatitude, normalizedLongitude, query.TimeZoneId, cancellationToken);
        return new ResolvedLocation(normalizedLatitude, normalizedLongitude, timeZone, source, place);
    }

    private static (double Latitude, double Longitude)? ValidateCoordinates(double? latitude, double? longitude)
    {
        if (latitude is null && longitude is null)
        {
            return null;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (latitude is not { } lat || !double.IsFinite(lat) || lat is < -90 or > 90)
        {
            errors["latitude"] = ["Latitude must be between -90 and 90 degrees and supplied together with longitude."];
        }

        if (longitude is not { } lon || !double.IsFinite(lon) || lon is < -180 or > 180)
        {
            errors["longitude"] = ["Longitude must be between -180 and 180 degrees and supplied together with latitude."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return (latitude!.Value, longitude!.Value);
    }

    private async Task<DateTimeZone> ResolveTimeZoneAsync(
        double latitude, double longitude, string? requestedTimeZoneId, CancellationToken cancellationToken)
    {
        DateTimeZone? requestedZone = null;
        if (!string.IsNullOrWhiteSpace(requestedTimeZoneId))
        {
            requestedZone = timeZones.GetZoneOrNull(requestedTimeZoneId.Trim())
                ?? throw new ValidationException("timeZone", "The time zone must be a valid IANA time zone identifier.");
        }

        var coordinateZoneId = await timeZoneLookup.FindTimeZoneIdAsync(latitude, longitude, cancellationToken);
        var coordinateZone = coordinateZoneId is null ? null : timeZones.GetZoneOrNull(coordinateZoneId);

        if (coordinateZone is null)
        {
            return requestedZone
                ?? throw new BusinessRuleException("The time zone for the requested location could not be determined.");
        }

        if (requestedZone is not null && requestedZone.Id != coordinateZone.Id)
        {
            LogRequestedTimeZoneOverridden(requestedZone.Id, coordinateZone.Id);
        }

        return coordinateZone;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Requested time zone {RequestedTimeZone} replaced by coordinate time zone {ResolvedTimeZone}")]
    private partial void LogRequestedTimeZoneOverridden(string requestedTimeZone, string resolvedTimeZone);
}
