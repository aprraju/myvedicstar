namespace MyVedicStar.Settings.Data;

public sealed record SavedLocationData(Guid LocationId, Guid UserId, string? City, string? State, string? Country,
    string? CountryCode, string? PostalCode, double Latitude, double Longitude, string TimeZone);
public sealed record SettingsData(Guid UserId, string? DefaultTradition, Guid? DefaultLocationId, long Revision,
    SavedLocationData? DefaultLocation);
public interface ISettingsRepository
{
    Task<SettingsData> ReadAsync(Guid userId, CancellationToken token);
    Task<SettingsData> SaveAsync(Guid userId, string? tradition, Guid? locationId, long revision, CancellationToken token);
    Task<IReadOnlyList<SavedLocationData>> LocationsAsync(Guid userId, CancellationToken token);
    Task<SavedLocationData> AddAsync(SavedLocationData location, CancellationToken token);
    Task<SettingsData> DeleteAsync(Guid userId, Guid locationId, CancellationToken token);
}
