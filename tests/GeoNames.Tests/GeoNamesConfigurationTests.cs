using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GeoNames.Tests;

public sealed class GeoNamesConfigurationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("demo")]
    [InlineData("invalid account")]
    public void Enabled_RejectsInvalidAccount(string username)
    {
        using var services = CreateServices(true, username);
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<GeoNamesOptions>>().Value);
    }

    [Fact]
    public async Task Disabled_DoesNotContactProvider()
    {
        using var services = CreateServices(false, "");
        var provider = services.GetRequiredService<ILocationSearchProvider>();
        var error = await Assert.ThrowsAsync<GeoNamesUnavailableException>(() => provider.SearchAsync("Austin", null, "en", 8, default));
        Assert.Equal(503, error.StatusCode);
    }

    private static ServiceProvider CreateServices(bool enabled, string username)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GeoNames:Enabled"] = enabled.ToString(),
            ["GeoNames:Username"] = username,
        }).Build();
        return new ServiceCollection().AddLogging().AddGeoNamesLocationSearch(configuration).BuildServiceProvider();
    }
}
