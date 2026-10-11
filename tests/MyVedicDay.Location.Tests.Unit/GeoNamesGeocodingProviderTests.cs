using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Location.Integration;

namespace MyVedicDay.Location.Tests.Unit;

/// <summary>
/// Checks the offline GeoNames geocoder through its public contract.
/// </summary>
public sealed class GeoNamesGeocodingProviderTests
{
    private static readonly IGeocodingProvider Geocoder = new ServiceCollection()
        .AddLogging()
        .AddLocationIntegration(new ConfigurationBuilder().Build())
        .BuildServiceProvider()
        .GetRequiredService<IGeocodingProvider>();

    [Theory]
    [InlineData("Austin", "Texas", "USA", "US", 30.27, -97.74)]
    [InlineData("Austin", "TX", null, "US", 30.27, -97.74)]
    [InlineData("San Jose", "California", "United States", "US", 37.34, -121.89)]
    [InlineData("Hyderabad", "Telangana", "India", "IN", 17.38, 78.46)]
    [InlineData("Hyderabad", "Sindh", null, "PK", 25.40, 68.37)]
    [InlineData("Bangalore", "Karnataka", "IN", "IN", 12.97, 77.59)]
    [InlineData("Sao Paulo", null, "Brazil", "BR", -23.55, -46.64)]
    [InlineData("St. Louis", "Missouri", "US", "US", 38.63, -90.20)]
    public async Task Search_FindsCity(string city, string? state, string? country, string countryCode, double latitude, double longitude)
    {
        var results = await Geocoder.SearchAsync(new PlaceSearch(city, state, country), 1, CancellationToken.None);

        var place = Assert.Single(results);
        Assert.Equal(countryCode, place.CountryCode);
        Assert.InRange(place.Latitude, latitude - 0.1, latitude + 0.1);
        Assert.InRange(place.Longitude, longitude - 0.1, longitude + 0.1);
    }

    [Fact]
    public async Task Search_PrefersPrimaryNameOverHistoricAlternateName()
    {
        // "Waterloo" is also a historic alternate name of Austin, Texas.
        var results = await Geocoder.SearchAsync(new PlaceSearch("Waterloo", "Ontario", "Canada"), 1, CancellationToken.None);

        Assert.Equal("CA", Assert.Single(results).CountryCode);
    }

    [Fact]
    public async Task Search_ReturnsStateAndCountryNames()
    {
        var place = (await Geocoder.SearchAsync(new PlaceSearch("austin", "texas", "us"), 1, CancellationToken.None))[0];

        Assert.Equal("Austin", place.City);
        Assert.Equal("Texas", place.State);
        Assert.Equal("United States", place.Country);
        Assert.Equal("America/Chicago", place.TimeZoneId);
    }

    [Theory]
    [InlineData("Qwertyzxville", null, null)]
    [InlineData("Austin", "Ontario", null)]
    [InlineData("Austin", "Texas", "Narnia")]
    public async Task Search_NoMatch_ReturnsEmpty(string city, string? state, string? country)
    {
        Assert.Empty(await Geocoder.SearchAsync(new PlaceSearch(city, state, country), 1, CancellationToken.None));
    }

    [Fact]
    public async Task FindNearest_ReturnsNearbyCity()
    {
        var place = await Geocoder.FindNearestAsync(17.385, 78.4867, 50, CancellationToken.None);

        Assert.NotNull(place);
        Assert.Equal("IN", place.CountryCode);
        Assert.Equal("Telangana", place.State);
    }

    [Fact]
    public async Task FindNearest_InOpenOcean_ReturnsNull()
    {
        Assert.Null(await Geocoder.FindNearestAsync(0, -140, 50, CancellationToken.None));
    }
}
