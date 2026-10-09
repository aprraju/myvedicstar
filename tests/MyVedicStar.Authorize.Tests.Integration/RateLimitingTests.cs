using System.Net;
using System.Net.Http.Headers;
using MyVedicStar.Authorize.Integration.Identity;

namespace MyVedicStar.Authorize.Tests.Integration;

/// <summary>Host that allows two API requests per client per window.</summary>
public sealed class LowRateLimitApiFactory : AuthorizeApiFactory
{
    protected override bool UserPersistenceEnabled => false;

    protected override int RateLimitPermitLimit => 2;
}

public sealed class RateLimitingTests : IClassFixture<LowRateLimitApiFactory>
{
    private const string ObjectId = "11111111-2222-3333-4444-555555555555";

    private readonly LowRateLimitApiFactory _factory;
    private readonly HttpClient _client;

    public RateLimitingTests(LowRateLimitApiFactory factory)
    {
        _factory = factory;
        _factory.ResetFakes();
        _factory.ExternalIdentityService.AddProfile(new ExternalUserProfile(ObjectId, "john.smith@example.com", "John", "Smith"));
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UserInfo_OverLimitFromOneClient_Returns429WithRetryAfterAndProblemDetails()
    {
        Assert.Equal(HttpStatusCode.OK, (await PostUserInfoAsync("203.0.113.10")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostUserInfoAsync("203.0.113.10")).StatusCode);

        var rejected = await PostUserInfoAsync("203.0.113.10");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UserInfo_LimitIsCountedPerClient()
    {
        await PostUserInfoAsync("203.0.113.20");
        await PostUserInfoAsync("203.0.113.20");

        var otherClient = await PostUserInfoAsync("203.0.113.21");

        Assert.Equal(HttpStatusCode.OK, otherClient.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_IsNeverRateLimited()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
            request.Headers.Add(AuthorizeApiFactory.RemoteIpTestHeader, "203.0.113.30");

            Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(request)).StatusCode);
        }
    }

    private Task<HttpResponseMessage> PostUserInfoAsync(string clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _factory.CreateToken([("oid", ObjectId)]));
        request.Headers.Add(AuthorizeApiFactory.RemoteIpTestHeader, clientIp);
        return _client.SendAsync(request);
    }
}
