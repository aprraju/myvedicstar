using System.Net;

namespace MyVedicStar.Authorize.Tests.Integration;

/// <summary>Container probe endpoints. Uses the persistence-disabled host so readiness has no database to reach.</summary>
public sealed class HealthEndpointTests : IClassFixture<PersistenceDisabledApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(PersistenceDisabledApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoint_WithoutToken_ReturnsHealthyStatusOnly(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
