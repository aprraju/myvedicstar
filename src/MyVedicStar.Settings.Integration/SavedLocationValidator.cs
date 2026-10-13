using GeoNames;
using Microsoft.Extensions.DependencyInjection;
namespace MyVedicStar.Settings.Integration;

public interface ISavedLocationValidator
{
    Task<bool> HasMatchingTimeZoneAsync(double latitude, double longitude, string timeZone, CancellationToken token);
}
internal sealed class SavedLocationValidator(ILocationSearchProvider provider) : ISavedLocationValidator
{
    public async Task<bool> HasMatchingTimeZoneAsync(double latitude, double longitude, string timeZone, CancellationToken token)
    {
        try { return (await provider.TimezoneAsync(latitude, longitude, token)).TimeZone == timeZone; }
        catch (GeoNamesUnavailableException) { throw new MyVedicStar.Shared.Exceptions.IntegrationException("Location validation is temporarily unavailable. Try saving again later."); }
    }
}
public static class SettingsIntegrationRegistration
{
    public static IServiceCollection AddSettingsIntegration(this IServiceCollection services)
    { services.AddScoped<ISavedLocationValidator, SavedLocationValidator>(); return services; }
}
