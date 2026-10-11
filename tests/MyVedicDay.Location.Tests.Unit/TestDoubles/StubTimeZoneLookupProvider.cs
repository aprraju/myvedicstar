using MyVedicDay.Location.Integration;

namespace MyVedicDay.Location.Tests.Unit.TestDoubles;

/// <summary>
/// Returns a fixed time zone identifier regardless of coordinates.
/// </summary>
internal sealed class StubTimeZoneLookupProvider(string? timeZoneId) : ITimeZoneLookupProvider
{
    public ValueTask<string?> FindTimeZoneIdAsync(double latitude, double longitude, CancellationToken cancellationToken) =>
        ValueTask.FromResult(timeZoneId);
}
