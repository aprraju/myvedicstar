using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MyVedicStar.Authorize.Tests.Integration;

public sealed class LoginLogoutEndpointTests : IClassFixture<AuthorizeApiFactory>
{
    private readonly AuthorizeApiFactory _factory;
    private readonly HttpClient _client;

    public LoginLogoutEndpointTests(AuthorizeApiFactory factory)
    {
        _factory = factory;
        factory.ResetFakes();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LoginUserInfoAndLogout_DeduplicateSessionEventsAndPreserveResponse()
    {
        var token = Token("session-1");
        var response = await Send("login", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["id", "name", "email", "firstName", "lastName"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Example", json.RootElement.GetProperty("name").GetString());
        await Send("login", token);
        await Send("userinfo", token);
        Assert.Equal(1, Assert.Single(_factory.UserStore.Logins).Type);
        Assert.Equal(HttpStatusCode.NoContent, (await Send("logout", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send("logout", token)).StatusCode);
        await Send("userinfo", token);
        Assert.Equal([1, 99], _factory.UserStore.Logins.Select(entry => entry.Type));
        await Send("login", Token("session-2"));
        Assert.Equal([1, 99, 1], _factory.UserStore.Logins.Select(entry => entry.Type));
    }

    [Fact]
    public async Task ClientSessionHeader_DeduplicatesTokensWithoutStableSessionClaims()
    {
        var session = Guid.NewGuid().ToString();
        var first = _factory.CreateToken([("oid", "user"), ("iat", "100")]);
        var second = _factory.CreateToken([("oid", "user"), ("iat", "200")]);
        Assert.Equal(HttpStatusCode.OK, (await Send("login", first, session)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send("userinfo", second, session)).StatusCode);
        await Send("logout", second, session);
        await Send("logout", first, session);
        Assert.Equal([1, 99], _factory.UserStore.Logins.Select(entry => entry.Type));
    }

    [Fact]
    public async Task InactiveAccount_LoginIsForbiddenAndDoesNotAppendAudit()
    {
        var token = Token("session-1");
        await Send("login", token);
        _factory.UserStore.Deactivate(_factory.UserStore.FindUserId("user")!.Value);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send("login", Token("session-2"))).StatusCode);
        Assert.Single(_factory.UserStore.Logins);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("logout")]
    public async Task Endpoints_RequireAuthentication(string route) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync($"/api/v1/auth/{route}", null)).StatusCode);

    [Fact]
    public async Task LogoutUnknownAccount_DoesNotRegisterUserOrCreateAudit()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Send("logout", Token("session-1"))).StatusCode);
        Assert.Null(_factory.UserStore.FindUserId("user"));
        Assert.Empty(_factory.UserStore.Logins);
    }

    private string Token(string session) => _factory.CreateToken([("oid", "user"), ("name", "Example"), ("email", "user@example.com"), ("sid", session)]);

    private Task<HttpResponseMessage> Send(string route, string token, string? session = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auth/{route}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (session is not null) request.Headers.Add("X-Auth-Session-Id", session);
        return _client.SendAsync(request);
    }
}
