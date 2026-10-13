using System.Net;
using System.Net.Http.Json;
using GeoNames;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Api.Tests.Integration.Infrastructure;

namespace MyVedicDay.Api.Tests.Integration.Api;

public sealed class GeoNamesApiTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    [Theory]
    [InlineData("search?text=Austin&country=US&language=en&maxRows=2", "results")]
    [InlineData("postal?text=78701&country=US", "results")]
    [InlineData("reverse?latitude=30.2672&longitude=-97.7431", "place")]
    [InlineData("timezone?latitude=30.2672&longitude=-97.7431", "timeZone")]
    public async Task Routes_ReturnFrontendContractsWithAuthentication(string query, string property)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<ILocationSearchProvider>(new StubProvider())));
        using var client = host.CreateClient();
        using var response = await client.GetAsync(new Uri($"/api/v1/locations/{query}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(body.TryGetProperty(property, out _));
    }

    [Theory]
    [InlineData("search?text=A")]
    [InlineData("search?text=Austin&maxRows=9")]
    [InlineData("postal?text=78701&country=USA")]
    [InlineData("reverse?latitude=91&longitude=0")]
    [InlineData("timezone?latitude=0")]
    public async Task Routes_RejectInvalidInputBeforeProvider(string query)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri($"/api/v1/locations/{query}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Quota_ReturnsRetryAfterAndGenericProblem()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<ILocationSearchProvider>(new StubProvider { Quota = true })));
        using var client = host.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/v1/locations/search?text=Austin", UriKind.Relative));
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(120), response.Headers.RetryAfter?.Delta);
        Assert.Contains("Location lookup is temporarily unavailable", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private sealed class StubProvider : ILocationSearchProvider
    {
        public bool Quota { get; init; }
        private static readonly LocationSuggestion Place = new("4671654", "Austin", "Texas", "United States", "US", null, 30.2672, -97.7431, "city");
        public Task<LocationSearchResponse> SearchAsync(string text, string? country, string? language, int maxRows, CancellationToken cancellationToken) =>
            Quota ? Task.FromException<LocationSearchResponse>(new GeoNamesUnavailableException(429, 120)) : Task.FromResult(new LocationSearchResponse([Place]));
        public Task<LocationSearchResponse> PostalAsync(string text, string? country, int maxRows, CancellationToken cancellationToken) => Task.FromResult(new LocationSearchResponse([Place with { PostalCode = text, Source = "postal" }]));
        public Task<LocationReverseResponse> ReverseAsync(double latitude, double longitude, string? language, CancellationToken cancellationToken) => Task.FromResult(new LocationReverseResponse(Place));
        public Task<LocationTimezoneResponse> TimezoneAsync(double latitude, double longitude, CancellationToken cancellationToken) => Task.FromResult(new LocationTimezoneResponse("America/Chicago"));
    }
}
