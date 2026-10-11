using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using MyVedicStar.ApiProtection.Abstractions;
using MyVedicStar.ApiProtection.Auth.Claims;
using MyVedicDay.Api.Tests.Integration.Infrastructure;

namespace MyVedicDay.Api.Tests.Integration.Api;

/// <summary>
/// Edge protection from MyVedicStar.ApiProtection (ADR-0014): named sliding-window rate limits per endpoint group,
/// health probes never limited, request size, one correlation ID per request, trusted-proxy validation.
/// </summary>
public sealed class ApiProtectionTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    private const string Hyderabad = "latitude=17.385&longitude=78.4867";

    [Fact]
    public async Task PublicReadLimit_RejectsWithProblemDetails()
    {
        using var client = Client(new() { ["ApiProtection:RateLimiting:PublicRead:PermitLimit"] = "2" });

        using var first = await client.GetAsync(new Uri("/api/v1/traditions", UriKind.Relative));
        using var second = await client.GetAsync(new Uri("/api/v1/traditions", UriKind.Relative));
        using var third = await client.GetAsync(new Uri("/api/v1/traditions", UriKind.Relative));

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, second.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
        Assert.Equal(429, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task AiExplanation_HasTheStricterExpensivePolicy_AndGoodTimesKeepTheirOwn()
    {
        using var client = Client(new() { ["ApiProtection:RateLimiting:ExpensiveAstrology:PermitLimit"] = "1" });
        var explanation = new Uri($"/api/v1/muhurta/good-times/explanation?{Hyderabad}&date=2026-10-01&activity=business", UriKind.Relative);
        var goodTimes = new Uri($"/api/v1/muhurta/good-times?{Hyderabad}&date=2026-10-01&activity=business", UriKind.Relative);

        using var allowed = await client.GetAsync(explanation);
        using var limited = await client.GetAsync(explanation);
        using var other = await client.GetAsync(goodTimes);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task HealthProbes_AreNeverRateLimited()
    {
        using var client = Client(new() { ["ApiProtection:RateLimiting:Global:PermitLimit"] = "1" });

        for (var i = 0; i < 5; i++)
        {
            using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
            using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (live.StatusCode, ready.StatusCode));
        }
    }

    [Fact]
    public async Task OversizedBody_IsRejectedWith413()
    {
        using var client = factory.CreateClient();
        using var content = new ByteArrayContent(new byte[70_000]);

        using var response = await client.PostAsync(new Uri("/api/v1/traditions", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task OneCorrelationIdPerRequest_SuppliedOrGeneratedByThePackage()
    {
        using var client = factory.CreateClient();

        using var supplied = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/traditions", UriKind.Relative));
        supplied.Headers.Add("X-Correlation-ID", "ui-request-42");
        using var echoed = await client.SendAsync(supplied);
        using var generated = await client.GetAsync(new Uri("/api/v1/traditions", UriKind.Relative));

        Assert.Equal(["ui-request-42"], echoed.Headers.GetValues("X-Correlation-ID"));
        Assert.True(Guid.TryParse(Assert.Single(generated.Headers.GetValues("X-Correlation-ID")), out _));
    }

    [Fact]
    public async Task UnsafeCorrelationId_IsReplaced_AndProblemDetailsCarryTheSameId()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/panchang/today?latitude=95&longitude=0", UriKind.Relative));
        request.Headers.Add("X-Correlation-ID", "bad.id with spaces");

        using var response = await client.SendAsync(request);

        var id = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
        Assert.NotEqual("bad.id with spaces", id);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public void InvalidTrustedProxy_FailsStartup()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ApiProtection:TrustedProxies:0", "not-an-ip"));

        Assert.ThrowsAny<OptionsValidationException>(() => host.CreateClient());
    }

    [Fact]
    public async Task ExpensiveConcurrency_RejectsAtCapacity_AndRecoversAfterRelease()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ApiProtection:Concurrency:PerUserExpensiveLimit", "1"));
        using var client = host.CreateClient();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(StandardClaimNames.UserId, "test-user-id"), new Claim("iss", "https://test.example")], "Test")),
        };
        var key = host.Services.GetRequiredService<IClientIdentityResolver>().Resolve(context);
        var concurrency = host.Services.GetRequiredService<IConcurrencyProtectionService>();
        var uri = new Uri($"/api/v1/muhurta/good-times/explanation?{Hyderabad}&date=2026-10-01&activity=business", UriKind.Relative);

        await using (var lease = await concurrency.TryAcquireAsync(key, CancellationToken.None))
        {
            Assert.True(lease.IsAcquired);
            using var rejected = await client.GetAsync(uri);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
            var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(503, problem.GetProperty("status").GetInt32());
        }

        using var recovered = await client.GetAsync(uri);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    private HttpClient Client(Dictionary<string, string> settings)
    {
        var host = factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });
        return host.CreateClient();
    }
}
