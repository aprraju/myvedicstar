using System.ComponentModel.DataAnnotations;

namespace MyVedicStar.API.Authorization.Options;

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

    /// <summary>
    /// Advertise only <c>https://</c> server URLs in the OpenAPI document. Enabled wherever TLS terminates in front
    /// of the app (the Container Apps ingress); disabled for local development over plain HTTP.
    /// </summary>
    public bool HttpsServerUrlsOnly { get; set; }

    /// <summary>Description of the bearer token callers must send.</summary>
    [Required]
    public string BearerTokenDescription { get; set; } = string.Empty;
}
