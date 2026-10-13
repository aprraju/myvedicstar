using System.Net;
using MyVedicDay.Api.Tests.Integration.Infrastructure;

namespace MyVedicDay.Api.Tests.Integration.Api;

public sealed class AnonymousAccessTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    [Theory]
    [InlineData("/api/v1/panchang/today")]
    [InlineData("/api/v1/locations/resolve")]
    public async Task FormerPublicEndpoints_RequireAuthentication(string path)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");

        // Authentication runs before validation even when request parameters are invalid.
        using var response = await client.GetAsync(new Uri($"{path}?latitude=95&longitude=0", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/panchang/tomorrow")]
    [InlineData("/api/v1/panchang/ten-days")]
    [InlineData("/api/v1/panchang/range")]
    [InlineData("/api/v1/panchang/month/2026/10")]
    [InlineData("/api/v1/panchang/year/2026")]
    [InlineData("/api/v1/panchang/2026-10-10")]
    [InlineData("/api/v1/panchang/traditions")]
    [InlineData("/api/v1/traditions")]
    [InlineData("/api/v1/panchang/glossary")]
    [InlineData("/api/v1/festivals/upcoming")]
    [InlineData("/api/v1/muhurta/activities")]
    [InlineData("/api/v1/muhurta/nakshatras")]
    [InlineData("/api/v1/muhurta/good-times")]
    [InlineData("/api/v1/muhurta/good-times/explanation")]
    [InlineData("/api/v1/muhurta/day-summaries")]
    [InlineData("/api/v1/muhurta/good-times/best")]
    public async Task OtherEndpoints_RequireAuthentication(string path)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
