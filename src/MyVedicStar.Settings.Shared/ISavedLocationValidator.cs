namespace MyVedicStar.Settings.Integration;

public interface ISavedLocationValidator
{
    Task<bool> HasMatchingTimeZoneAsync(double latitude, double longitude, string timeZone, CancellationToken token);
}
