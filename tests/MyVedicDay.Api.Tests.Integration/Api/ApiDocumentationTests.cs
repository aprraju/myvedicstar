using System.Net;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MyVedicDay.Api.Tests.Integration.Api;

/// <summary>
/// Guards the published OpenAPI document and Swagger UI (on by default in every environment; the test host runs Development).
/// </summary>
public sealed class ApiDocumentationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task OpenApiDocument_DescribesEveryOperationWithExamples()
    {
        using var document = await GetOpenApiDocumentAsync();
        var root = document.RootElement;

        Assert.Equal("MyVedicDay API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.Contains("GeoNames", root.GetProperty("info").GetProperty("description").GetString(), StringComparison.Ordinal);

        var operations = root.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => operation.Value))
            .ToArray();

        Assert.Equal(22, operations.Length);
        foreach (var operation in operations)
        {
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("operationId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("description").GetString()));

            foreach (var parameter in operation.GetProperty("parameters").EnumerateArray())
            {
                Assert.False(string.IsNullOrWhiteSpace(parameter.GetProperty("description").GetString()));
                Assert.True(parameter.TryGetProperty("example", out _), $"{parameter.GetProperty("name")} has no example.");
            }

            foreach (var response in operation.GetProperty("responses").EnumerateObject())
            {
                Assert.False(string.IsNullOrWhiteSpace(response.Value.GetProperty("description").GetString()));
            }

            var success = operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json");
            Assert.True(success.TryGetProperty("example", out _));
        }
    }

    [Fact]
    public async Task OpenApiDocument_UsesCamelCaseQueryParameterNames()
    {
        using var document = await GetOpenApiDocumentAsync();

        var names = document.RootElement.GetProperty("paths").GetProperty("/api/v1/panchang/today").GetProperty("get")
            .GetProperty("parameters").EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString() ?? string.Empty).ToArray();

        Assert.Equal(new[] { "place", "latitude", "longitude", "timeZone", "tradition", "language" }, names);
    }

    [Fact]
    public async Task OpenApiDocument_PublishesOnlyBrowserCompatiblePatterns()
    {
        using var document = await GetOpenApiDocumentAsync();

        // Swagger UI validates "pattern" with JavaScript RegExp (no 'u' flag), which does not understand \p{...}.
        var patterns = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .SelectMany(operation => operation.Value.GetProperty("parameters").EnumerateArray())
            .Where(parameter => parameter.GetProperty("schema").TryGetProperty("pattern", out _))
            .Select(parameter => parameter.GetProperty("schema").GetProperty("pattern").GetString() ?? string.Empty);

        Assert.All(patterns, pattern => Assert.DoesNotContain(@"\p{", pattern, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SwaggerUi_IsServedWithAPolicyThatAllowsItsScripts()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("script-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.OK)]
    [InlineData("false", HttpStatusCode.Unauthorized)]
    public async Task OutsideDevelopment_DocumentationIsOnByDefault_AndCanBeTurnedOff(string? enabled, HttpStatusCode expected)
    {
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            if (enabled is not null)
            {
                builder.UseSetting("ApiDocumentation:Enabled", enabled);
            }
        });
        using var client = production.CreateClient();

        using var swagger = await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));
        using var document = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        Assert.Equal(expected, swagger.StatusCode);
        Assert.Equal(expected, document.StatusCode);
    }

    private async Task<JsonDocument> GetOpenApiDocumentAsync()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
