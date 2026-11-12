using Microsoft.Extensions.DependencyInjection;
namespace GeoNames.Business;

/// <summary>Public business boundary for worldwide location lookup use cases.</summary>
public interface ILocationLookupFacade
{
    Task<LocationSearchResponse> SearchAsync(string text, string? country, string? language, int maxRows, CancellationToken cancellationToken);
    Task<LocationSearchResponse> PostalAsync(string text, string? country, int maxRows, CancellationToken cancellationToken);
    Task<LocationReverseResponse> ReverseAsync(double latitude, double longitude, string? language, CancellationToken cancellationToken);
    Task<LocationTimezoneResponse> TimezoneAsync(double latitude, double longitude, CancellationToken cancellationToken);
}

internal sealed class LocationLookupFacade(ILocationSearchProvider provider) : ILocationLookupFacade
{
    public Task<LocationSearchResponse> SearchAsync(string text, string? country, string? language, int maxRows, CancellationToken cancellationToken) => provider.SearchAsync(text, country, language, maxRows, cancellationToken);
    public Task<LocationSearchResponse> PostalAsync(string text, string? country, int maxRows, CancellationToken cancellationToken) => provider.PostalAsync(text, country, maxRows, cancellationToken);
    public Task<LocationReverseResponse> ReverseAsync(double latitude, double longitude, string? language, CancellationToken cancellationToken) => provider.ReverseAsync(latitude, longitude, language, cancellationToken);
    public Task<LocationTimezoneResponse> TimezoneAsync(double latitude, double longitude, CancellationToken cancellationToken) => provider.TimezoneAsync(latitude, longitude, cancellationToken);
}

public static class LocationLookupRegistration
{
    public static IServiceCollection AddLocationLookupBusiness(this IServiceCollection services) =>
        services.AddScoped<ILocationLookupFacade, LocationLookupFacade>();
}
