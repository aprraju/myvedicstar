using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Api.Options;

namespace MyVedicStar.Authorize.Api.OpenApi;

internal static class ApiDocumentationExtensions
{
    /// <summary>Registers the OpenAPI document, described by <see cref="ApiDocumentationOptions"/>.</summary>
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiDocumentationOptions>()
            .Bind(configuration.GetSection(ApiDocumentationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<DocumentInfoTransformer>();
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });

        return services;
    }

    /// <summary>
    /// Publishes the OpenAPI document and Swagger UI anonymously in every environment. The document describes
    /// the API surface only; every operation still requires a valid bearer token.
    /// </summary>
    public static WebApplication UseApiDocumentation(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ApiDocumentationOptions>>().Value;

        app.MapOpenApi(options.OpenApiRoutePattern).AllowAnonymous();
        app.UseSwaggerUI(swaggerUi =>
        {
            swaggerUi.RoutePrefix = options.SwaggerUiRoutePrefix;
            swaggerUi.DocumentTitle = options.Title;
            swaggerUi.SwaggerEndpoint(options.SwaggerEndpointUrl, options.Title);
        });

        return app;
    }
}
