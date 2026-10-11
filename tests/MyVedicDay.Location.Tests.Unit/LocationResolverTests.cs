using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Location.Integration;
using MyVedicDay.Location.Tests.Unit.TestDoubles;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Location.Tests.Unit;

public sealed class LocationResolverTests
{
    [Fact]
    public async Task Resolve_NormalizesCoordinatesToConfiguredPrecision()
    {
        using var host = CreateHost("Asia/Kolkata");

        var location = await Resolve(host, 17.38501, 78.48667, null);

        Assert.Equal(17.39, location.Latitude);
        Assert.Equal(78.49, location.Longitude);
    }

    [Fact]
    public async Task Resolve_NearbyCoordinates_NormalizeToTheSameLocation()
    {
        using var host = CreateHost("Asia/Kolkata");

        var first = await Resolve(host, 17.3861, 78.4867, null);
        var second = await Resolve(host, 17.3899, 78.4912, null);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(90.1, 0, "latitude")]
    [InlineData(-91, 0, "latitude")]
    [InlineData(0, 180.5, "longitude")]
    [InlineData(double.NaN, 0, "latitude")]
    public async Task Resolve_InvalidCoordinates_ThrowsValidationException(double latitude, double longitude, string field)
    {
        using var host = CreateHost("Asia/Kolkata");

        var exception = await Assert.ThrowsAsync<ValidationException>(() => Resolve(host, latitude, longitude, null));

        Assert.True(exception.Errors.ContainsKey(field));
    }

    [Fact]
    public async Task Resolve_UnknownRequestedTimeZone_ThrowsValidationException()
    {
        using var host = CreateHost("Asia/Kolkata");

        var exception = await Assert.ThrowsAsync<ValidationException>(() => Resolve(host, 17.39, 78.49, "Mars/Olympus_Mons"));

        Assert.True(exception.Errors.ContainsKey("timeZone"));
    }

    [Fact]
    public async Task Resolve_RequestedTimeZoneDisagreesWithCoordinates_UsesCoordinateTimeZone()
    {
        using var host = CreateHost("Asia/Kolkata");

        var location = await Resolve(host, 17.39, 78.49, "America/Chicago");

        Assert.Equal("Asia/Kolkata", location.TimeZone.Id);
    }

    [Fact]
    public async Task Resolve_NoCoordinateTimeZone_FallsBackToValidRequestedTimeZone()
    {
        using var host = CreateHost(null);

        var location = await Resolve(host, 0, -140, "Pacific/Pitcairn");

        Assert.Equal("Pacific/Pitcairn", location.TimeZone.Id);
    }

    [Fact]
    public async Task Resolve_NoTimeZoneAvailable_ThrowsBusinessRuleException()
    {
        using var host = CreateHost(null);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Resolve(host, 0, -140, null));
    }

    private static LocationTestHost CreateHost(string? coordinateTimeZone) =>
        new(services => services.AddSingleton<ITimeZoneLookupProvider>(new StubTimeZoneLookupProvider(coordinateTimeZone)));

    private static Task<ResolvedLocation> Resolve(LocationTestHost host, double latitude, double longitude, string? timeZone) =>
        host.GetRequiredService<ILocationResolver>().ResolveAsync(new LocationQuery(latitude, longitude, TimeZoneId: timeZone), CancellationToken.None);
}
