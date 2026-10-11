using System.Net;
using System.Text.Json;

namespace MyVedicStar.Authorize.Tests.Integration;

/// <summary>The OpenAPI document and Swagger UI are published anonymously in every environment.</summary>
public sealed class ApiDocumentationTests : IClassFixture<PersistenceDisabledApiFactory>
{
    private readonly PersistenceDisabledApiFactory _factory;
    private readonly HttpClient _client;

    public ApiDocumentationTests(PersistenceDisabledApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task OpenApiDocument_WithoutToken_DescribesApiWithConfiguredTitleAndBearerScheme()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("MyVedicDay API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.True(root.GetProperty("paths").TryGetProperty("/api/v1/auth/userinfo", out _));
        Assert.Equal(
            "JWT Bearer token from any supported identity provider: Google, Azure Entra External ID, Apple, or Facebook.",
            root.GetProperty("components").GetProperty("securitySchemes").GetProperty("BearerAuth").GetProperty("description").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_BehindTlsTerminatingProxy_AdvertisesHttpsServer()
    {
        // The ingress terminates TLS and calls the container over plain HTTP, keeping the original Host header.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.myvedicstar.com/openapi/v1.json");
        request.Headers.Add(AuthorizeApiFactory.RemoteIpTestHeader, AuthorizeApiFactory.TrustedProxyIp);
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var server = Assert.Single(document.RootElement.GetProperty("servers").EnumerateArray());
        Assert.Equal("https://api.myvedicstar.com/", server.GetProperty("url").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_ForwardedProtoFromUntrustedSource_IsIgnored()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.myvedicstar.com/openapi/v1.json");
        request.Headers.Add(AuthorizeApiFactory.RemoteIpTestHeader, "198.51.100.9");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var server = Assert.Single(document.RootElement.GetProperty("servers").EnumerateArray());
        Assert.Equal("http://api.myvedicstar.com/", server.GetProperty("url").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_HttpsServerUrlsOnly_AdvertisesHttpsWithoutForwardedProto()
    {
        using var client = _factory
            .WithWebHostBuilder(builder => builder.UseSetting("ApiDocumentation:HttpsServerUrlsOnly", "true"))
            .CreateClient();

        var response = await client.GetAsync("http://api.myvedicstar.com:8080/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var server = Assert.Single(document.RootElement.GetProperty("servers").EnumerateArray());
        Assert.Equal("https://api.myvedicstar.com/", server.GetProperty("url").GetString());
    }

    [Fact]
    public async Task SwaggerUi_WithoutToken_IsServed()
    {
        var response = await _client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
