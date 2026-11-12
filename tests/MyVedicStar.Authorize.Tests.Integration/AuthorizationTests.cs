using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.Auth.Authorization;

namespace MyVedicStar.Authorize.Tests.Integration;

/// <summary>Every controller requires a signed-in user whose token carries the API scope.</summary>
public sealed class AuthorizationTests : IClassFixture<PersistenceDisabledApiFactory>
{
    private const string ObjectId = "11111111-2222-3333-4444-555555555555";

    private readonly PersistenceDisabledApiFactory _factory;
    private readonly HttpClient _client;

    public AuthorizationTests(PersistenceDisabledApiFactory factory)
    {
        _factory = factory;
        _factory.ResetFakes();
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("access_as_user")]
    [InlineData("openid profile access_as_user")]
    public async Task UserInfo_TokenWithApiScope_IsAllowed(string scope)
    {
        var response = await PostUserInfoAsync(_factory.CreateToken([("oid", ObjectId)], scope: scope));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Panchang.Read")]
    [InlineData("access_as_user_extra")]
    [InlineData("ACCESS_AS_USER")]
    public async Task UserInfo_TokenWithoutApiScope_Returns403ProblemDetails(string scope)
    {
        var response = await PostUserInfoAsync(_factory.CreateToken([("oid", ObjectId)], scope: scope));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UserInfo_AppOnlyTokenWithRolesButNoScope_Returns403()
    {
        var response = await PostUserInfoAsync(_factory.CreateToken([("oid", ObjectId), ("roles", "access_as_user"), ("idtyp", "app")], scope: null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserInfo_WithoutToken_Returns401()
    {
        var response = await _client.PostAsync("/api/v1/auth/userinfo", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void AuthorizationControllerActions_RequireTheApiScopePolicyAndNoneAllowAnonymous()
    {
        var actions = typeof(MyVedicStar.API.Controllers.AuthController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .Where(type => type == typeof(MyVedicStar.API.Controllers.AuthController))
            .SelectMany(controller => controller.GetMethods().Where(method => method.DeclaringType == controller && method.IsPublic)
                .Select(action => (Controller: controller, Action: action)))
            .ToList();

        Assert.NotEmpty(actions);
        Assert.All(actions, entry =>
        {
            var policies = entry.Controller.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Concat(entry.Action.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true))
                .Cast<AuthorizeAttribute>()
                .Select(attribute => attribute.Policy);
            Assert.Contains(AuthPolicyNames.ApiScope, policies);
            Assert.Empty(entry.Controller.GetCustomAttributes(typeof(IAllowAnonymous), inherit: true));
            Assert.Empty(entry.Action.GetCustomAttributes(typeof(IAllowAnonymous), inherit: true));
        });
    }

    [Fact]
    public void Convention_RejectsAnyAnonymousControllerAction()
    {
        var application = new Microsoft.AspNetCore.Mvc.ApplicationModels.ApplicationModel();
        var controller = new Microsoft.AspNetCore.Mvc.ApplicationModels.ControllerModel(
            typeof(AnonymousTestController).GetTypeInfo(), [new AllowAnonymousAttribute()]);
        controller.Actions.Add(new Microsoft.AspNetCore.Mvc.ApplicationModels.ActionModel(
            typeof(AnonymousTestController).GetMethod(nameof(AnonymousTestController.Get))!, []));
        application.Controllers.Add(controller);

        var exception = Assert.Throws<InvalidOperationException>(() => new DenyAnonymousControllersConvention().Apply(application));

        Assert.Contains("AnonymousTestController.Get", exception.Message);
    }

    private Task<HttpResponseMessage> PostUserInfoAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    [NonController]
    public sealed class AnonymousTestController : ControllerBase
    {
        public IActionResult Get() => Ok();
    }
}
