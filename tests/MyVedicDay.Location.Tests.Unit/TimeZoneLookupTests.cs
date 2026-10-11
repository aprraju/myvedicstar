using MyVedicDay.Location.Integration;
using MyVedicDay.Location.Tests.Unit.TestDoubles;

namespace MyVedicDay.Location.Tests.Unit;

/// <summary>The offline time zone lookup: a zone for any coordinate, and cancellation honoured.</summary>
public sealed class TimeZoneLookupTests
{
    [Theory]
    [InlineData(17.385, 78.4867, "Asia/Kolkata")]
    [InlineData(30.2672, -97.7431, "America/Chicago")]
    [InlineData(-33.8688, 151.2093, "Australia/Sydney")]
    public async Task Coordinates_MapToTheirIanaZone(double latitude, double longitude, string expected)
    {
        using var host = new LocationTestHost();

        var zone = await host.GetRequiredService<ITimeZoneLookupProvider>().FindTimeZoneIdAsync(latitude, longitude, CancellationToken.None);

        Assert.Equal(expected, zone);
    }

    [Fact]
    public async Task Cancellation_IsHonoured()
    {
        using var host = new LocationTestHost();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await host.GetRequiredService<ITimeZoneLookupProvider>().FindTimeZoneIdAsync(17.385, 78.4867, cancelled.Token));
    }
}
