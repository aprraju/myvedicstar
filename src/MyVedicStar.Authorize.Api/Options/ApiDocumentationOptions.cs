using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Api.Options;

/// <summary>OpenAPI document and Swagger UI settings.</summary>
public sealed class ApiDocumentationOptions
{
    public const string SectionName = "ApiDocumentation";

    /// <summary>Whether the OpenAPI document and Swagger UI are exposed.</summary>
    public bool Enabled { get; set; }

    /// <summary>Route pattern for the OpenAPI document, for example <c>/openapi/{documentName}.json</c>.</summary>
    [Required]
    public string OpenApiRoutePattern { get; set; } = string.Empty;

    /// <summary>URL Swagger UI loads the document from, for example <c>/openapi/v1.json</c>.</summary>
    [Required]
    public string SwaggerEndpointUrl { get; set; } = string.Empty;

    /// <summary>Title shown in Swagger UI.</summary>
    [Required]
    public string Title { get; set; } = string.Empty;
}
