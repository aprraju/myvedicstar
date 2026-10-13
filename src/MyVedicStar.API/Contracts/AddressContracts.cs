using MyVedicStar.Authorize.Business;
namespace MyVedicStar.API.Contracts;

public sealed record AddressOptionResponse(string Code, string Name)
{
    public static AddressOptionResponse From(AddressOption value) => new(value.Code, value.Name);
}
public sealed record AddressOptionsResponse(IReadOnlyList<AddressOptionResponse> Results);
public sealed record AddressPlaceResponse(string Id, string City, string? Region, string Country, string CountryCode,
    string? PostalCode, double Latitude, double Longitude, string Source)
{
    public static AddressPlaceResponse From(AddressPlace value) => new(value.Id, value.City, value.Region, value.Country,
        value.CountryCode, value.PostalCode, value.Latitude, value.Longitude, value.Source);
}
public sealed record AddressPlacesResponse(IReadOnlyList<AddressPlaceResponse> Results);
