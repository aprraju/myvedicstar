using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVedicDay.Location.Integration;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Location;

/// <summary>
/// Resolves "City, State[, Country]" text to a city, and coordinates to the nearest named city.
/// </summary>
/// <remarks>
/// Search order when no country is given:
/// <list type="number">
/// <item><description>the country of the supplied coordinates (browser location);</description></item>
/// <item><description>anywhere, most populous first (the user may be travelling);</description></item>
/// <item><description>"City, X" with X read as a country instead of a state (e.g. "Hyderabad, India").</description></item>
/// </list>
/// </remarks>
internal sealed partial class PlaceResolver(
    IGeocodingProvider geocoder, IOptions<LocationOptions> options, ILogger<PlaceResolver> logger)
{
    private const string PlaceFormatMessage = "Enter the place as \"City, State\" or \"City, State, Country\".";

    public async Task<GeocodedPlace> FindPlaceAsync(
        string placeText, (double Latitude, double Longitude)? near, CancellationToken cancellationToken)
    {
        var (city, state, country) = Parse(placeText);

        string? countryHint = null;
        if (country is null && near is { } coordinates)
        {
            var nearest = await geocoder.FindNearestAsync(
                coordinates.Latitude, coordinates.Longitude, options.Value.CountryHintMaxDistanceKm, cancellationToken);
            countryHint = nearest?.CountryCode;
        }

        foreach (var search in GetSearches(city, state, country, countryHint))
        {
            var matches = await geocoder.SearchAsync(search, 1, cancellationToken);
            if (matches.Count > 0)
            {
                LogPlaceResolved(matches[0].CountryCode, countryHint is not null);
                return matches[0];
            }
        }

        throw new ValidationException("place", $"No matching city was found. {PlaceFormatMessage}");
    }

    public async Task<Place?> FindNearestCityAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var nearest = await geocoder.FindNearestAsync(latitude, longitude, options.Value.NearestCityMaxDistanceKm, cancellationToken);
        return nearest is null ? null : ToPlace(nearest);
    }

    public static Place ToPlace(GeocodedPlace place) => new(place.City, place.State, place.Country, place.CountryCode);

    private static (string City, string? State, string? Country) Parse(string placeText)
    {
        var parts = placeText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => (parts[0], null, null),
            2 => (parts[0], parts[1], null),
            3 => (parts[0], parts[1], parts[2]),
            _ => throw new ValidationException("place", PlaceFormatMessage),
        };
    }

    private static IEnumerable<PlaceSearch> GetSearches(string city, string? state, string? country, string? countryHint)
    {
        if (country is not null)
        {
            yield return new PlaceSearch(city, state, country);
            yield break;
        }

        if (countryHint is not null)
        {
            yield return new PlaceSearch(city, state, countryHint);
        }

        yield return new PlaceSearch(city, state);

        if (state is not null)
        {
            yield return new PlaceSearch(city, Country: state);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Place search resolved to country {CountryCode} (browser country hint used: {CountryHintUsed})")]
    private partial void LogPlaceResolved(string countryCode, bool countryHintUsed);
}
