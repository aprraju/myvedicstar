using System.Net;
using System.Text.Json;

namespace MyVedicStar.Authorize.Tests.Integration;

/// <summary>The OpenAPI document and Swagger UI are published anonymously in every environment.</summary>
public sealed class ApiDocumentationTests : IClassFixture<PersistenceDisabledApiFactory>
{
    private readonly HttpClient _client;

    public ApiDocumentationTests(PersistenceDisabledApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task OpenApiDocument_WithoutToken_DescribesApiWithConfiguredTitleAndBearerScheme()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("MyVedicStar Authorize API v1", root.GetProperty("info").GetProperty("title").GetString());
        Assert.True(root.GetProperty("paths").TryGetProperty("/api/v1/auth/userinfo", out _));
        Assert.Equal(
            "Access token issued by Microsoft Entra External ID.",
            root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("description").GetString());
    }

    [Fact]
    public async Task SwaggerUi_WithoutToken_IsServed()
    {
        var response = await _client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
