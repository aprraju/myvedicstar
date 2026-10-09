using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.Authorize.Api.Options;

/// <summary>OpenAPI document and Swagger UI settings. Documentation is published in every environment.</summary>
public sealed class ApiDocumentationOptions
{
    public const string SectionName = "ApiDocumentation";

    /// <summary>Route pattern for the OpenAPI document, for example <c>/openapi/{documentName}.json</c>.</summary>
    [Required]
    public string OpenApiRoutePattern { get; set; } = string.Empty;

    /// <summary>URL Swagger UI loads the document from, for example <c>/openapi/v1.json</c>.</summary>
    [Required]
    public string SwaggerEndpointUrl { get; set; } = string.Empty;

    /// <summary>Path Swagger UI is served under, without slashes, for example <c>swagger</c>.</summary>
    [Required]
    [RegularExpression("^[A-Za-z0-9_-]+(/[A-Za-z0-9_-]+)*$")]
    public string SwaggerUiRoutePrefix { get; set; } = string.Empty;

    /// <summary>API title shown in the OpenAPI document and Swagger UI.</summary>
    [Required]
    public string Title { get; set; } = string.Empty;

    /// <summary>API description shown in the OpenAPI document.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Description of the bearer token callers must send.</summary>
    [Required]
    public string BearerTokenDescription { get; set; } = string.Empty;
}
