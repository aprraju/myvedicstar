using System.Net;
using System.Net.Http.Headers;

namespace MyVedicStar.Authorize.Tests.Integration;

public sealed class SecurityTests : IClassFixture<PersistenceDisabledApiFactory>
{
    private const string ObjectId = "11111111-2222-3333-4444-555555555555";

    private readonly PersistenceDisabledApiFactory _factory;
    private readonly HttpClient _client;

    public SecurityTests(PersistenceDisabledApiFactory factory)
    {
        _factory = factory;
        _factory.ResetFakes();
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/api/v1/auth/userinfo")]
    public async Task EveryResponse_HasSecurityHeadersAndNoServerHeader(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task UnmappedPath_WithoutToken_IsRejectedByFallbackPolicy()
    {
        var response = await _client.GetAsync("/api/v1/not-a-real-endpoint");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UserInfo_ResponseIsNeverCached()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _factory.CreateToken([("oid", ObjectId)]));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
