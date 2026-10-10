using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace MyVedicStar.Authorize.Tests.Integration;

/// <summary>Correlation IDs and request-size limits from MyVedicStar.ApiProtection, as wired into this API.</summary>
public sealed class ApiProtectionTests : IClassFixture<PersistenceDisabledApiFactory>
{
    private const string ObjectId = "11111111-2222-3333-4444-555555555555";
    private const string CorrelationHeader = "X-Correlation-ID";

    private readonly PersistenceDisabledApiFactory _factory;
    private readonly HttpClient _client;

    public ApiProtectionTests(PersistenceDisabledApiFactory factory)
    {
        _factory = factory;
        _factory.ResetFakes();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Response_HasGeneratedCorrelationId()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.True(Guid.TryParse(Assert.Single(response.Headers.GetValues(CorrelationHeader)), out _));
    }

    [Fact]
    public async Task SafeIncomingCorrelationId_IsEchoed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationHeader, "client-request-42");

        var response = await _client.SendAsync(request);

        Assert.Equal("client-request-42", Assert.Single(response.Headers.GetValues(CorrelationHeader)));
    }

    [Fact]
    public async Task UserInfo_BodyOverStandardLimit_Returns413ProblemDetails()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/userinfo")
        {
            Content = new ByteArrayContent(new byte[70_000]),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _factory.CreateToken([("oid", ObjectId)]));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}

/// <summary>Host whose liveness path is missing from <c>ApiProtection:HealthCheckPaths</c>.</summary>
public sealed class UnlistedHealthPathApiFactory : AuthorizeApiFactory
{
    protected override bool UserPersistenceEnabled => false;

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("HealthChecks:LivenessPath", "/health/unlisted");
    }
}

public sealed class HealthPathValidationTests
{
    [Fact]
    public void Startup_FailsWhenAHealthPathIsNotExemptFromRateLimiting()
    {
        using var factory = new UnlistedHealthPathApiFactory();

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validation = Assert.IsType<OptionsValidationException>(exception is AggregateException aggregate ? aggregate.InnerException : exception);
        Assert.Contains("ApiProtection:HealthCheckPaths", validation.Message);
    }
}
