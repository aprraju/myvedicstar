namespace MyVedicDay.Location.Integration.GeoTimeZone;

/// <summary>
/// Offline coordinate → IANA time zone lookup backed by the GeoTimeZone boundary dataset.
/// No network calls, so no retry or circuit breaker is needed. The library does not throw: callers validate
/// coordinates first (see <c>LocationResolver</c>).
/// </summary>
internal sealed class GeoTimeZoneLookupProvider : ITimeZoneLookupProvider
{
    public ValueTask<string?> FindTimeZoneIdAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = global::GeoTimeZone.TimeZoneLookup.GetTimeZone(latitude, longitude).Result;
        return ValueTask.FromResult(string.IsNullOrWhiteSpace(result) ? null : result);
    }
}
