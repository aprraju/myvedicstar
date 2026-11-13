using MyVedicStar.Settings.Business;
namespace MyVedicStar.API.Contracts;

/// <summary>Atomic revision-checked save. Omitted/null preference codes preserve stored values (defaults on insertion). Codes are case-insensitive; empty, labels and unsupported codes fail validation.</summary>
public sealed record SaveSettingsRequest(string? DefaultTradition, Guid? DefaultLocationId, long Revision, string? ChartStyle = null, string? Ayanamsa = null);
public sealed record SavedLocationRequest(string? City, string? State, string? Country, string? CountryCode,
    string? PostalCode, double Latitude, double Longitude, string TimeZone);
public sealed record SavedLocationResponse(Guid LocationId, string? City, string? State, string? Country,
    string? CountryCode, string? PostalCode, double Latitude, double Longitude, string TimeZone)
{
    public static SavedLocationResponse From(SavedLocation value) => new(value.LocationId, value.City, value.State, value.Country, value.CountryCode, value.PostalCode, value.Latitude, value.Longitude, value.TimeZone);
}
public sealed record SettingsResponse(string? DefaultTradition, Guid? DefaultLocationId, long Revision, SavedLocationResponse? DefaultLocation, string ChartStyle, string Ayanamsa)
{
    public static SettingsResponse From(UserSettings value) => new(value.DefaultTradition, value.DefaultLocationId, value.Revision, value.DefaultLocation is null ? null : SavedLocationResponse.From(value.DefaultLocation), value.ChartStyle, value.Ayanamsa);
}
