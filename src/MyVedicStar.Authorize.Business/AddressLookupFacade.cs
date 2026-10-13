using GeoNames;
using MyVedicStar.Shared.Exceptions;
namespace MyVedicStar.Authorize.Business;

public sealed record AddressOption(string Code, string Name);
public sealed record AddressPlace(string Id, string City, string? Region, string Country, string CountryCode,
    string? PostalCode, double Latitude, double Longitude, string Source);
/// <summary>Public business boundary for geographic address suggestions.</summary>
public interface IAddressLookupFacade
{
    Task<IReadOnlyList<AddressOption>> CountriesAsync(CancellationToken token);
    Task<IReadOnlyList<AddressOption>> RegionsAsync(string country, CancellationToken token);
    Task<IReadOnlyList<AddressPlace>> CitiesAsync(string text, string country, string? region, CancellationToken token);
}
internal sealed class AddressLookupFacade(ILocationSearchProvider provider) : IAddressLookupFacade
{
    public Task<IReadOnlyList<AddressOption>> CountriesAsync(CancellationToken token) => Options(() => provider.CountriesAsync(token));
    public Task<IReadOnlyList<AddressOption>> RegionsAsync(string country, CancellationToken token) => Options(() => provider.RegionsAsync(country, token));
    private static async Task<IReadOnlyList<AddressOption>> Options(Func<Task<GeographyOptions>> operation)
    {
        try { return (await operation()).Results.Select(x => new AddressOption(x.Code, x.Name)).ToArray(); }
        catch (GeoNamesUnavailableException) { throw new IntegrationException("Address suggestions are temporarily unavailable. Manual entry is available."); }
    }
    public async Task<IReadOnlyList<AddressPlace>> CitiesAsync(string text, string country, string? region, CancellationToken token)
    {
        try { return (await provider.AddressCitiesAsync(text, country, region, token)).Results.Select(x => new AddressPlace(x.Id, x.City, x.Region, x.Country, x.CountryCode, x.PostalCode, x.Latitude, x.Longitude, x.Source)).ToArray(); }
        catch (GeoNamesUnavailableException) { throw new IntegrationException("Address suggestions are temporarily unavailable. Manual entry is available."); }
    }
}
