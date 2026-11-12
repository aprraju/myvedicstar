namespace MyVedicDay.Location.Integration;

/// <summary>
/// Resolves the IANA time zone that applies at a geographic coordinate.
/// </summary>
public interface ITimeZoneLookupProvider
{
    /// <summary>
    /// Returns the IANA time zone identifier for the coordinate, or <see langword="null"/> when none can be determined.
    /// </summary>
    /// <exception cref="MyVedicDay.Shared.Exceptions.IntegrationException">The provider failed.</exception>
    ValueTask<string?> FindTimeZoneIdAsync(double latitude, double longitude, CancellationToken cancellationToken);
}
