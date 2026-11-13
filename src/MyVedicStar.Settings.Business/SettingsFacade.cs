using System.Security.Claims;
using MyVedicStar.Authorize.Business;
using MyVedicStar.Settings.Data;
using MyVedicStar.Settings.Integration;
using MyVedicStar.Shared.Exceptions;
using MyVedicDay.Traditions;
namespace MyVedicStar.Settings.Business;

public sealed record SavedLocation(Guid LocationId, string? City, string? State, string? Country,
    string? CountryCode, string? PostalCode, double Latitude, double Longitude, string TimeZone);
public sealed record UserSettings(string? DefaultTradition, Guid? DefaultLocationId, long Revision, SavedLocation? DefaultLocation, string ChartStyle = PreferenceCodes.DefaultChartStyle, string Ayanamsa = PreferenceCodes.DefaultAyanamsa);
public sealed record SaveSettings(string? DefaultTradition, Guid? DefaultLocationId, long Revision, string? ChartStyle = null, string? Ayanamsa = null);
public sealed record AddSavedLocation(string? City, string? State, string? Country, string? CountryCode,
    string? PostalCode, double Latitude, double Longitude, string TimeZone);
/// <summary>Only entry point into settings and saved-location business operations.</summary>
public interface ISettingsFacade
{
    Task<UserSettings> GetAsync(ClaimsPrincipal user, CancellationToken token);
    Task<UserSettings> SaveAsync(ClaimsPrincipal user, SaveSettings request, CancellationToken token);
    Task<IReadOnlyList<SavedLocation>> LocationsAsync(ClaimsPrincipal user, CancellationToken token);
    Task<SavedLocation> AddAsync(ClaimsPrincipal user, AddSavedLocation request, CancellationToken token);
    Task<UserSettings> DeleteAsync(ClaimsPrincipal user, Guid locationId, CancellationToken token);
}
internal sealed class SettingsFacade(IAuthorizeFacade accounts, ISettingsRepository repository,
    ITraditionRegistry traditions, ISavedLocationValidator validator) : ISettingsFacade
{
    public async Task<UserSettings> GetAsync(ClaimsPrincipal user, CancellationToken token) => Map(await repository.ReadAsync(await accounts.GetRegisteredAccountIdAsync(user, token), token));
    public async Task<UserSettings> SaveAsync(ClaimsPrincipal user, SaveSettings request, CancellationToken token)
    {
        if (request.Revision < 0) throw new ValidationException("Invalid settings revision.");
        var chartStyle = Canonicalize(request.ChartStyle, PreferenceCodes.ChartStyles, PreferenceCodes.InvalidChartStyle);
        var ayanamsa = Canonicalize(request.Ayanamsa, PreferenceCodes.Ayanamsas, PreferenceCodes.InvalidAyanamsa);
        var id = await accounts.GetRegisteredAccountIdAsync(user, token);
        var tradition = string.IsNullOrWhiteSpace(request.DefaultTradition) ? null : request.DefaultTradition.Trim();
        if (tradition is not null)
            tradition = (await traditions.GetKeysAsync(token)).FirstOrDefault(key => key.Equals(tradition, StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException("Choose a supported tradition.");
        return Map(await repository.SaveAsync(id, tradition, request.DefaultLocationId, request.Revision, token, chartStyle, ayanamsa));
    }
    public async Task<IReadOnlyList<SavedLocation>> LocationsAsync(ClaimsPrincipal user, CancellationToken token) =>
        (await repository.LocationsAsync(await accounts.GetRegisteredAccountIdAsync(user, token), token)).Select(Map).ToArray();
    public async Task<SavedLocation> AddAsync(ClaimsPrincipal user, AddSavedLocation request, CancellationToken token)
    {
        var id = await accounts.GetRegisteredAccountIdAsync(user, token);
        if (!double.IsFinite(request.Latitude) || request.Latitude is < -90 or > 90 || !double.IsFinite(request.Longitude) || request.Longitude is < -180 or > 180 || string.IsNullOrWhiteSpace(request.TimeZone) || request.TimeZone.Length > 64)
            throw new ValidationException("Choose a valid location and timezone.");
        if (request.TimeZone != "UTC" && !TimeZoneInfo.TryConvertIanaIdToWindowsId(request.TimeZone, out _)) throw new ValidationException("Choose a valid IANA timezone.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone); }
        catch (TimeZoneNotFoundException) { throw new ValidationException("Choose a valid IANA timezone."); }
        if (!await validator.HasMatchingTimeZoneAsync(request.Latitude, request.Longitude, request.TimeZone, token)) throw new ValidationException("The timezone does not match the selected location.");
        static string? Text(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max && !value.Any(char.IsControl) ? value.Trim() : throw new ValidationException("Location text is invalid or too long.");
        return Map(await repository.AddAsync(new(Guid.Empty, id, Text(request.City, 256), Text(request.State, 256), Text(request.Country, 256), Text(request.CountryCode, 2), Text(request.PostalCode, 20), request.Latitude, request.Longitude, request.TimeZone), token));
    }
    public async Task<UserSettings> DeleteAsync(ClaimsPrincipal user, Guid locationId, CancellationToken token) => Map(await repository.DeleteAsync(await accounts.GetRegisteredAccountIdAsync(user, token), locationId, token));
    private static string? Canonicalize(string? value, IReadOnlyList<string> supported, string message) =>
        value is null ? null : supported.FirstOrDefault(code => code.Equals(value, StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException(message);
    private static UserSettings Map(SettingsData row) => new(row.DefaultTradition, row.DefaultLocationId, row.Revision, row.DefaultLocation is null ? null : Map(row.DefaultLocation), row.ChartStyle, row.Ayanamsa);
    private static SavedLocation Map(SavedLocationData row) => new(row.LocationId, row.City, row.State, row.Country, row.CountryCode, row.PostalCode, row.Latitude, row.Longitude, row.TimeZone);
}
internal sealed class DisabledSettingsFacade : ISettingsFacade
{
    private static IntegrationException Disabled() => new("Account settings are unavailable.");
    public Task<UserSettings> GetAsync(ClaimsPrincipal user, CancellationToken token) => throw Disabled();
    public Task<UserSettings> SaveAsync(ClaimsPrincipal user, SaveSettings request, CancellationToken token) => throw Disabled();
    public Task<IReadOnlyList<SavedLocation>> LocationsAsync(ClaimsPrincipal user, CancellationToken token) => throw Disabled();
    public Task<SavedLocation> AddAsync(ClaimsPrincipal user, AddSavedLocation request, CancellationToken token) => throw Disabled();
    public Task<UserSettings> DeleteAsync(ClaimsPrincipal user, Guid id, CancellationToken token) => throw Disabled();
}
