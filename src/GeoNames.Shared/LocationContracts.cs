namespace GeoNames;

public sealed record LocationSuggestion(string Id, string City, string? Region, string Country,
    string CountryCode, string? PostalCode, double Latitude, double Longitude, string Source);
public sealed record LocationSearchResponse(IReadOnlyList<LocationSuggestion> Results);
public sealed record LocationReverseResponse(LocationSuggestion? Place);
public sealed record LocationTimezoneResponse(string TimeZone);

public sealed record GeographyOption(string Code, string Name);
public sealed record GeographyOptions(IReadOnlyList<GeographyOption> Results);

public interface ILocationSearchProvider
{
    Task<GeographyOptions> CountriesAsync(CancellationToken token) => throw new NotSupportedException();
    Task<GeographyOptions> RegionsAsync(string country, CancellationToken token) => throw new NotSupportedException();
    Task<LocationSearchResponse> AddressCitiesAsync(string text, string country, string? region, CancellationToken token) => SearchAsync(text, country, null, 8, token);
    Task<LocationSearchResponse> SearchAsync(string text, string? country, string? language, int maxRows, CancellationToken cancellationToken);
    Task<LocationSearchResponse> PostalAsync(string text, string? country, int maxRows, CancellationToken cancellationToken);
    Task<LocationReverseResponse> ReverseAsync(double latitude, double longitude, string? language, CancellationToken cancellationToken);
    Task<LocationTimezoneResponse> TimezoneAsync(double latitude, double longitude, CancellationToken cancellationToken);
}

public sealed class GeoNamesUnavailableException(int statusCode = 503, int? retryAfterSeconds = null, string? detail = null) : Exception("Location lookup is temporarily unavailable.")
{
    public int StatusCode { get; } = statusCode;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
    public string? Detail { get; } = detail;
}
