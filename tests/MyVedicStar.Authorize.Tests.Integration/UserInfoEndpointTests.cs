using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MyVedicStar.Authorize.Integration.Identity;

namespace MyVedicStar.Authorize.Tests.Integration;

public sealed class UserInfoEndpointTests : IClassFixture<AuthorizeApiFactory>
{
    private const string UserInfoPath = "/api/v1/auth/userinfo";
    private const string ObjectId = "11111111-2222-3333-4444-555555555555";

    private static readonly ExternalUserProfile GraphProfile = new(
        ObjectId, "john.smith@example.com", "John", "Smith");

    private readonly AuthorizeApiFactory _factory;
    private readonly HttpClient _client;

    public UserInfoEndpointTests(AuthorizeApiFactory factory)
    {
        _factory = factory;
        _factory.ResetFakes();
        _factory.ExternalIdentityService.AddProfile(GraphProfile);
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UserInfo_ValidToken_ReturnsMinimalProfileWithInternalId()
    {
        var response = await PostUserInfoAsync(ValidToken());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            ["id", "givenName", "surname", "name", "email"],
            body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(_factory.UserStore.FindUserId(ObjectId)?.ToString(), body.GetProperty("id").GetString());
        Assert.Equal("John", body.GetProperty("givenName").GetString());
        Assert.Equal("Smith", body.GetProperty("surname").GetString());
        Assert.Equal("John Smith", body.GetProperty("name").GetString());
        Assert.Equal("john.smith@example.com", body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task UserInfo_ValidToken_StoresProfileAndRecordsOneLoginInOneTransaction()
    {
        await PostUserInfoAsync(ValidToken());

        Assert.Equal(
            new Data.Users.UserProfileData(ObjectId, "john.smith@example.com", "John", "Smith"),
            _factory.UserStore.FindUser(ObjectId));

        var login = Assert.Single(_factory.UserStore.Logins);
        Assert.Equal(_factory.UserStore.FindUserId(ObjectId), login.UserId);
        Assert.Equal(1, _factory.UserStore.TransactionCount);
    }

    [Fact]
    public async Task UserInfo_CalledTwice_KeepsSameUserAndRecordsTwoLogins()
    {
        var first = await (await PostUserInfoAsync(ValidToken())).Content.ReadFromJsonAsync<JsonElement>();
        _factory.ExternalIdentityService.AddProfile(GraphProfile with { Surname = "Smith-Jones" });
        var second = await (await PostUserInfoAsync(ValidToken())).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(first.GetProperty("id").GetString(), second.GetProperty("id").GetString());
        Assert.Equal("Smith-Jones", _factory.UserStore.FindUser(ObjectId)?.Surname);
        Assert.Equal(2, _factory.UserStore.Logins.Count);
    }

    [Fact]
    public async Task UserInfo_RequestFromTrustedProxy_RecordsForwardedClientIp()
    {
        var response = await PostUserInfoAsync(ValidToken(), remoteIp: AuthorizeApiFactory.TrustedProxyIp, forwardedFor: "203.0.113.7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), Assert.Single(_factory.UserStore.Logins).IpAddress);
    }

    [Fact]
    public async Task UserInfo_ForwardedHeaderFromUntrustedSource_IsIgnored()
    {
        var response = await PostUserInfoAsync(ValidToken(), remoteIp: "198.51.100.9", forwardedFor: "203.0.113.7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(IPAddress.Parse("198.51.100.9"), Assert.Single(_factory.UserStore.Logins).IpAddress);
    }

    [Fact]
    public async Task UserInfo_NoToken_Returns401AndStoresNothing()
    {
        var response = await _client.PostAsync(UserInfoPath, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_factory.UserStore.Logins);
    }

    [Fact]
    public async Task UserInfo_ExpiredToken_Returns401()
    {
        var response = await PostUserInfoAsync(_factory.CreateToken([("oid", ObjectId)], expiresUtc: DateTime.UtcNow.AddMinutes(-10)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UserInfo_WrongAudience_Returns401()
    {
        var response = await PostUserInfoAsync(_factory.CreateToken([("oid", ObjectId)], audience: "another-app"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UserInfo_TokenSignedByUnknownKey_Returns401()
    {
        var response = await PostUserInfoAsync(
            _factory.CreateToken([("oid", ObjectId)], signingKey: AuthorizeApiFactory.CreateKey("attacker-key")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UserInfo_TokenWithoutObjectId_Returns401ProblemDetails()
    {
        var response = await PostUserInfoAsync(_factory.CreateToken([("sub", "pairwise-subject")]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(_factory.UserStore.Logins);
    }

    [Fact]
    public async Task UserInfo_UserMissingFromDirectory_Returns404AndStoresNothing()
    {
        var response = await PostUserInfoAsync(_factory.CreateToken([("oid", "unknown-object-id")]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_factory.UserStore.Logins);
    }

    [Fact]
    public async Task UserInfo_GraphUnavailable_Returns503WithoutInternalDetails()
    {
        _factory.ExternalIdentityService.IsUnavailable = true;

        var response = await PostUserInfoAsync(ValidToken());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("MicrosoftGraph", await response.Content.ReadAsStringAsync());
        Assert.Empty(_factory.UserStore.Logins);
    }

    [Fact]
    public async Task UserInfo_Get_IsNotAllowed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, UserInfoPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ValidToken());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private string ValidToken() => _factory.CreateToken([("oid", ObjectId), ("sub", "pairwise-subject")]);

    private Task<HttpResponseMessage> PostUserInfoAsync(string token, string? remoteIp = null, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, UserInfoPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (remoteIp is not null)
        {
            request.Headers.Add(AuthorizeApiFactory.RemoteIpTestHeader, remoteIp);
        }

        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return _client.SendAsync(request);
    }
}
