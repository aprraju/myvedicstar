using MyVedicStar.Settings.Business;
namespace MyVedicStar.API.Contracts;

public sealed record SaveSettingsRequest(string? DefaultTradition, Guid? DefaultLocationId, long Revision);
public sealed record SavedLocationRequest(string? City, string? State, string? Country, string? CountryCode,
    string? PostalCode, double Latitude, double Longitude, string TimeZone);
public sealed record SavedLocationResponse(Guid LocationId, string? City, string? State, string? Country,
    string? CountryCode, string? PostalCode, double Latitude, double Longitude, string TimeZone)
{
    public static SavedLocationResponse From(SavedLocation value) => new(value.LocationId, value.City, value.State, value.Country, value.CountryCode, value.PostalCode, value.Latitude, value.Longitude, value.TimeZone);
}
public sealed record SettingsResponse(string? DefaultTradition, Guid? DefaultLocationId, long Revision, SavedLocationResponse? DefaultLocation)
{
    public static SettingsResponse From(UserSettings value) => new(value.DefaultTradition, value.DefaultLocationId, value.Revision, value.DefaultLocation is null ? null : SavedLocationResponse.From(value.DefaultLocation));
}
