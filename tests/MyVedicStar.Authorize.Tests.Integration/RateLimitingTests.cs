using System.Net;
using System.Net.Http.Headers;
using MyVedicStar.Authorize.Integration.Identity;

namespace MyVedicStar.Authorize.Tests.Integration;

/// <summary>Host that allows two <c>/userinfo</c> requests per user per window.</summary>
public sealed class LowRateLimitApiFactory : AuthorizeApiFactory
{
    protected override bool UserPersistenceEnabled => false;

    protected override int RateLimitPermitLimit => 2;
}

/// <summary><c>/userinfo</c> uses the package's <c>authenticated-standard</c> policy: limited per signed-in user.</summary>
public sealed class RateLimitingTests : IClassFixture<LowRateLimitApiFactory>
{
    private readonly LowRateLimitApiFactory _factory;
    private readonly HttpClient _client;

    public RateLimitingTests(LowRateLimitApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UserInfo_OverLimitForOneUser_Returns429ProblemDetails()
    {
        var objectId = AddUser();
        Assert.Equal(HttpStatusCode.OK, (await PostUserInfoAsync(objectId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostUserInfoAsync(objectId)).StatusCode);

        var rejected = await PostUserInfoAsync(objectId);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        // Sliding-window limiters attach no retry time, so Retry-After is optional; when present it must be positive.
        Assert.True(rejected.Headers.RetryAfter is null || rejected.Headers.RetryAfter.Delta > TimeSpan.Zero);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UserInfo_LimitIsCountedPerUserNotPerIp()
    {
        var firstUser = AddUser();
        await PostUserInfoAsync(firstUser, clientIp: "203.0.113.20");
        await PostUserInfoAsync(firstUser, clientIp: "203.0.113.20");

        var sameIpOtherUser = await PostUserInfoAsync(AddUser(), clientIp: "203.0.113.20");
        var otherIpSameUser = await PostUserInfoAsync(firstUser, clientIp: "203.0.113.21");

        Assert.Equal(HttpStatusCode.OK, sameIpOtherUser.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, otherIpSameUser.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoint_IsNeverRateLimited(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(path)).StatusCode);
        }
    }

    private string AddUser()
    {
        var objectId = Guid.NewGuid().ToString();
        _factory.ExternalIdentityService.AddProfile(new ExternalUserProfile(objectId, "john.smith@example.com", "John", "Smith"));
        return objectId;
    }

    private Task<HttpResponseMessage> PostUserInfoAsync(string objectId, string clientIp = "203.0.113.10")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _factory.CreateToken([("oid", objectId)]));
        request.Headers.Add(AuthorizeApiFactory.RemoteIpTestHeader, clientIp);
        return _client.SendAsync(request);
    }
}
