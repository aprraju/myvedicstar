using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MyVedicStar.Authorize.Tests.Integration;

public sealed class UserInfoEndpointTests : IClassFixture<AuthorizeApiFactory>
{
    private const string UserInfoPath = "/api/v1/auth/userinfo";
    private const string ObjectId = "11111111-2222-3333-4444-555555555555";
    private const string Name = "John,Smith";
    private const string Email = "john.smith@example.com";

    private readonly AuthorizeApiFactory _factory;
    private readonly HttpClient _client;

    public UserInfoEndpointTests(AuthorizeApiFactory factory)
    {
        _factory = factory;
        _factory.ResetFakes();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UserInfo_ValidToken_ReturnsOnlyIdNameAndEmailFromToken()
    {
        var response = await PostUserInfoAsync(ValidToken());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["id", "name", "email"], body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(ObjectId, body.GetProperty("id").GetString());
        Assert.Equal(Name, body.GetProperty("name").GetString());
        Assert.Equal(Email, body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task UserInfo_TokenWithoutNameOrEmail_ReturnsNulls()
    {
        var response = await PostUserInfoAsync(_factory.CreateToken([("oid", ObjectId)]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ObjectId, body.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("email").ValueKind);
    }

    [Theory]
    [InlineData("unknown", null, null, null)]
    [InlineData("Ignored Display Name", "Jane", "Doe", "Jane,Doe")]
    [InlineData("Ignored Display Name", "Jane", null, "Jane,")]
    [InlineData("Ignored Display Name", null, "Doe", ",Doe")]
    [InlineData("aprraju@gmail.com", "Jane", "Doe", "Jane,Doe")]
    public async Task UserInfo_PreferredUsernameEmail_ReturnsSeparateEmailAndUsableName(string name, string? given, string? family, string? expectedName)
    {
        var claims = new List<(string, string)> { ("oid", ObjectId), ("name", name), ("preferred_username", "aprraju@gmail.com") };
        if (given is not null) claims.Add(("given_name", given));
        if (family is not null) claims.Add(("family_name", family));
        var response = await PostUserInfoAsync(_factory.CreateToken(claims));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["id", "name", "email"], body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(ObjectId, body.GetProperty("id").GetString());
        Assert.Equal(expectedName, body.GetProperty("name").GetString());
        Assert.Equal("aprraju@gmail.com", body.GetProperty("email").GetString());
        Assert.Equal("aprraju@gmail.com", _factory.UserStore.FindUser(ObjectId)?.Email);
    }

    [Fact]
    public async Task UserInfo_ValidToken_StoresProfileAndRecordsOneLoginInOneTransaction()
    {
        await PostUserInfoAsync(ValidToken());

        Assert.Equal(
            new Data.Users.UserProfileData(ObjectId, Email, "John", "Smith", AuthorizeApiFactory.Issuer),
            _factory.UserStore.FindUser(ObjectId));

        var login = Assert.Single(_factory.UserStore.Logins);
        Assert.Equal(_factory.UserStore.FindUserId(ObjectId), login.UserId);
        Assert.Equal(1, _factory.UserStore.TransactionCount);
    }

    [Fact]
    public async Task UserInfo_CalledTwiceInSameSession_UpdatesProfileAndRecordsOneLogin()
    {
        var token = _factory.CreateToken([("oid", ObjectId), ("name", "Ignored Display Name"), ("given_name", "John"), ("family_name", "Smith"), ("email", Email), ("sid", "session-1")]);
        await PostUserInfoAsync(token);
        var firstUserId = _factory.UserStore.FindUserId(ObjectId);
        await PostUserInfoAsync(_factory.CreateToken([("oid", ObjectId), ("given_name", "John"), ("family_name", "Smith-Jones"), ("email", Email), ("sid", "session-1")]));

        Assert.Equal(firstUserId, _factory.UserStore.FindUserId(ObjectId));
        Assert.Equal("Smith-Jones", _factory.UserStore.FindUser(ObjectId)?.FamilyName);
        Assert.Single(_factory.UserStore.Logins);
    }

    [Fact]
    public async Task UserInfo_RequestFromTrustedProxy_RecordsForwardedClientIp()
    {
        var response = await PostUserInfoAsync(ValidToken(), remoteIp: AuthorizeApiFactory.TrustedProxyIp, forwardedFor: "203.0.113.7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), Assert.Single(_factory.UserStore.Logins).IpAddress);
    }

    [Fact]
    public async Task UserInfo_ExistingUser_SynchronizesFallbackEmailAndNullNameWithoutAnotherSessionAudit()
    {
        await PostUserInfoAsync(_factory.CreateToken([("oid", ObjectId), ("name", "Ignored Display Name"), ("given_name", "John"), ("family_name", "Smith"), ("email", Email), ("sid", "profile-session")]));
        var id = _factory.UserStore.FindUserId(ObjectId);
        var response = await PostUserInfoAsync(_factory.CreateToken([("oid", ObjectId), ("name", "unknown"), ("preferred_username", "aprraju@gmail.com"), ("sid", "profile-session")]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stored = _factory.UserStore.FindUser(ObjectId);
        Assert.Equal(body.GetProperty("name").GetString(), stored!.GivenName);
        Assert.Equal(body.GetProperty("email").GetString(), stored.Email);
        Assert.Null(stored.GivenName);
        Assert.Null(stored.FamilyName);
        Assert.Equal("aprraju@gmail.com", stored.Email);
        Assert.Equal(id, _factory.UserStore.FindUserId(ObjectId));
        Assert.Single(_factory.UserStore.Logins);
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
    public async Task UserInfo_Get_IsNotAllowed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, UserInfoPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ValidToken());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private string ValidToken() =>
        _factory.CreateToken([("oid", ObjectId), ("sub", "pairwise-subject"), ("name", "Ignored Display Name"), ("given_name", "John"), ("family_name", "Smith"), ("email", Email)]);

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
