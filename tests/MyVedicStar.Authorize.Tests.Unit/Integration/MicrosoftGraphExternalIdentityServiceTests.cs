using System.Net;
using System.Text;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using MyVedicStar.Authorize.Integration.Identity;
using MyVedicStar.Authorize.Integration.Identity.MicrosoftGraph;
using MyVedicStar.Shared.Exceptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MyVedicStar.Authorize.Tests.Unit.Integration;

public sealed class MicrosoftGraphExternalIdentityServiceTests
{
    private const string GraphScope = "https://graph.microsoft.com/.default";

    private readonly StubHttpMessageHandler _handler = new();
    private readonly TokenCredential _credential = Substitute.For<TokenCredential>();
    private readonly MicrosoftGraphExternalIdentityService _service;

    public MicrosoftGraphExternalIdentityServiceTests()
    {
        _credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(new AccessToken("app-token", DateTimeOffset.UtcNow.AddHours(1)));

        _service = new MicrosoftGraphExternalIdentityService(
            new HttpClient(_handler) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") },
            _credential,
            Microsoft.Extensions.Options.Options.Create(new MicrosoftGraphOptions { Scopes = [GraphScope] }),
            NullLogger<MicrosoftGraphExternalIdentityService>.Instance);
    }

    [Fact]
    public async Task GetUserProfileAsync_RequestsOnlySelectedPropertiesWithAppToken()
    {
        _handler.Respond(HttpStatusCode.OK, """{ "id": "object-id" }""");

        await _service.GetUserProfileAsync("object-id", CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/users/object-id?$select=id,mail,otherMails,identities,givenName,surname",
            request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("app-token", request.Headers.Authorization?.Parameter);
        await _credential.Received(1).GetTokenAsync(
            Arg.Is<TokenRequestContext>(context => context.Scopes.SequenceEqual(new[] { GraphScope })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUserProfileAsync_EscapesUserIdInPath()
    {
        _handler.Respond(HttpStatusCode.OK, """{ "id": "x" }""");

        await _service.GetUserProfileAsync("a/../b?c", CancellationToken.None);

        Assert.StartsWith("https://graph.microsoft.com/v1.0/users/a%2F..%2Fb%3Fc?", _handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetUserProfileAsync_MapsIdEmailGivenNameAndSurname()
    {
        _handler.Respond(HttpStatusCode.OK, """
            {
              "id": "object-id",
              "mail": "john.smith@example.com",
              "givenName": "John",
              "surname": "Smith"
            }
            """);

        var profile = await _service.GetUserProfileAsync("object-id", CancellationToken.None);

        Assert.Equal(new ExternalUserProfile("object-id", "john.smith@example.com", "John", "Smith"), profile);
    }

    [Fact]
    public async Task GetUserProfileAsync_NoMail_UsesEmailSignInIdentity()
    {
        _handler.Respond(HttpStatusCode.OK, """
            {
              "id": "object-id",
              "mail": null,
              "identities": [
                { "signInType": "federated", "issuer": "google.com", "issuerAssignedId": "123" },
                { "signInType": "emailAddress", "issuer": "tenant.onmicrosoft.com", "issuerAssignedId": "local@example.com" }
              ],
              "otherMails": [ "other@example.com" ]
            }
            """);

        var profile = await _service.GetUserProfileAsync("object-id", CancellationToken.None);

        Assert.Equal("local@example.com", profile.Email);
    }

    [Fact]
    public async Task GetUserProfileAsync_NoMailOrEmailIdentity_UsesOtherMails()
    {
        _handler.Respond(HttpStatusCode.OK, """{ "id": "object-id", "identities": [], "otherMails": [ "other@example.com" ] }""");

        var profile = await _service.GetUserProfileAsync("object-id", CancellationToken.None);

        Assert.Equal("other@example.com", profile.Email);
    }

    [Fact]
    public async Task GetUserProfileAsync_UserNotFound_ThrowsResourceNotFound()
    {
        _handler.Respond(HttpStatusCode.NotFound, """{ "error": { "code": "Request_ResourceNotFound" } }""");

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => _service.GetUserProfileAsync("object-id", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetUserProfileAsync_GraphError_ThrowsIntegrationExceptionWithoutLeakingDetails(HttpStatusCode statusCode)
    {
        _handler.Respond(statusCode, """{ "error": { "code": "Authorization_RequestDenied" } }""");

        var exception = await Assert.ThrowsAsync<IntegrationException>(() => _service.GetUserProfileAsync("object-id", CancellationToken.None));

        Assert.DoesNotContain("Graph", exception.PublicMessage);
    }

    [Fact]
    public async Task GetUserProfileAsync_TokenAcquisitionFails_ThrowsIntegrationException()
    {
        _credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Azure.Identity.AuthenticationFailedException("bad secret"));

        await Assert.ThrowsAsync<IntegrationException>(() => _service.GetUserProfileAsync("object-id", CancellationToken.None));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task GetUserProfileAsync_BlankUserId_ThrowsValidationWithoutCallingGraph()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _service.GetUserProfileAsync(" ", CancellationToken.None));
        Assert.Empty(_handler.Requests);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private string _body = "{}";

        public List<HttpRequestMessage> Requests { get; } = [];

        public void Respond(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
