using Microsoft.Extensions.Options;
using MyVedicDay.Location.Tests.Unit.TestDoubles;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Location.Tests.Unit;

/// <summary>
/// The three supported ways to specify a location: "City, State, Country"; "City, State" with browser coordinates;
/// browser coordinates alone.
/// </summary>
public sealed class PlaceLocationResolutionTests
{
    private const double KarachiLatitude = 24.86;
    private const double KarachiLongitude = 67.01;

    [Fact]
    public async Task CityStateCountry_ResolvesCoordinatesAndTimeZone()
    {
        var location = await ResolveAsync(new LocationQuery(Place: "Austin, Texas, USA"));

        Assert.Equal(LocationSource.Place, location.Source);
        Assert.Equal("America/Chicago", location.TimeZoneId);
        Assert.Equal("Austin, Texas, United States", location.Place?.DisplayName);
        Assert.Equal(30.27, location.Latitude);
    }

    [Fact]
    public async Task CityState_WithBrowserLocation_UsesTheBrowsersCountry()
    {
        // "Hyderabad" alone is ambiguous; a browser in Pakistan selects Hyderabad, Sindh.
        var location = await ResolveAsync(new LocationQuery(KarachiLatitude, KarachiLongitude, "Hyderabad"));

        Assert.Equal("PK", location.Place?.CountryCode);
        Assert.Equal("Asia/Karachi", location.TimeZoneId);
    }

    [Fact]
    public async Task CityState_WithoutBrowserLocation_PicksTheLargestMatch()
    {
        var location = await ResolveAsync(new LocationQuery(Place: "Hyderabad"));

        Assert.Equal("IN", location.Place?.CountryCode);
    }

    [Fact]
    public async Task CityState_BrowserInAnotherCountry_StillFindsTheCity()
    {
        // Browser in Pakistan, user asks for Austin, Texas: the country hint does not match, so search globally.
        var location = await ResolveAsync(new LocationQuery(KarachiLatitude, KarachiLongitude, "Austin, Texas"));

        Assert.Equal("America/Chicago", location.TimeZoneId);
        Assert.Equal(LocationSource.Place, location.Source);
    }

    [Fact]
    public async Task CityCountry_TreatsSecondPartAsCountryWhenNoStateMatches()
    {
        var location = await ResolveAsync(new LocationQuery(Place: "Hyderabad, Pakistan"));

        Assert.Equal("PK", location.Place?.CountryCode);
    }

    [Fact]
    public async Task BrowserLocationOnly_IsLabelledWithTheNearestCity()
    {
        var location = await ResolveAsync(new LocationQuery(17.385, 78.4867));

        Assert.Equal(LocationSource.Coordinates, location.Source);
        Assert.Equal("Asia/Kolkata", location.TimeZoneId);
        Assert.Equal("Telangana", location.Place?.State);
    }

    [Fact]
    public async Task NearestCityDistance_IsConfigurable()
    {
        // About 90 km south of Alice Springs: no city within the default 50 km.
        var query = new LocationQuery(-24.5, 133.9);

        var unnamed = await ResolveAsync(query);
        var named = await ResolveAsync(query, new Dictionary<string, string?> { ["Location:NearestCityMaxDistanceKm"] = "500" });

        Assert.Null(unnamed.Place);
        Assert.Equal("Australia/Darwin", unnamed.TimeZoneId);
        Assert.Equal("AU", named.Place?.CountryCode);
    }

    [Theory]
    [InlineData("Location:NearestCityMaxDistanceKm", "0")]
    [InlineData("Location:CountryHintMaxDistanceKm", "5000")]
    public async Task InvalidDistanceSettings_FailValidation(string key, string value)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(() =>
            ResolveAsync(new LocationQuery(17.385, 78.4867), new Dictionary<string, string?> { [key] = value }));
    }

    [Theory]
    [InlineData("Qwertyzxville, Nowhere")]
    [InlineData("a, b, c, d")]
    public async Task UnknownOrMalformedPlace_ThrowsValidationException(string place)
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(() => ResolveAsync(new LocationQuery(Place: place)));

        Assert.True(exception.Errors.ContainsKey("place"));
    }

    [Fact]
    public async Task NoPlaceAndNoCoordinates_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => ResolveAsync(new LocationQuery()));
    }

    private static async Task<LocationDetails> ResolveAsync(LocationQuery query, IDictionary<string, string?>? settings = null)
    {
        using var host = new LocationTestHost(settings: settings);
        return await host.GetRequiredService<ILocationFacade>().ResolveAsync(query, CancellationToken.None);
    }
}
