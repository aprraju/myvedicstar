namespace MyVedicDay.Api.Infrastructure.OpenApi;

/// <summary>
/// Registers and maps the OpenAPI document (<c>/openapi/v1.json</c>) and Swagger UI (<c>/swagger</c>).
/// </summary>
internal static class ApiDocumentationExtensions
{
    public const string DocumentPath = "/openapi/v1.json";
    public const string SwaggerUiPrefix = "swagger";

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiDocumentationOptions>().Bind(configuration.GetSection(ApiDocumentationOptions.SectionName));

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<ApiDocumentTransformer>();
            options.AddOperationTransformer<ParameterDocumentationTransformer>();
            options.AddOperationTransformer<ResponseExampleTransformer>();
            options.AddOperationTransformer<SecurityOperationTransformer>();
            options.AddOperationTransformer<GeoNamesOperationTransformer>();
        });

        return services;
    }

    /// <summary>
    /// Serves the Swagger UI anonymously. Call before authentication and authorization so the fallback policy never
    /// sees it: the page is static and holds no data.
    /// </summary>
    public static WebApplication UseApiDocumentationUi(this WebApplication app)
    {
        if (!IsEnabled(app))
        {
            return app;
        }

        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = SwaggerUiPrefix;
            options.SwaggerEndpoint(DocumentPath, "MyVedicDay API v1");
            options.DocumentTitle = "MyVedicDay API";
            options.DisplayRequestDuration();
            options.EnableTryItOutByDefault();
            options.DefaultModelsExpandDepth(1);
        });

        return app;
    }

    /// <summary>
    /// Maps the anonymous OpenAPI document when the documentation is enabled.
    /// </summary>
    public static WebApplication MapApiDocumentation(this WebApplication app)
    {
        if (!IsEnabled(app))
        {
            return app;
        }

        // Generating the document runs real calculations for its examples: rate-limited like the API everywhere but
        // Development, so Swagger cannot be used to load the service.
        // Anonymous so the anonymous Swagger UI can load it; the operations themselves still require the API scope.
        var document = app.MapOpenApi().AllowAnonymous();
        if (app.Environment.IsDevelopment())
        {
            document.DisableRateLimiting();
        }

        return app;
    }

    /// <summary>On in Development, or elsewhere when <c>ApiDocumentation:Enabled</c> is true.</summary>
    private static bool IsEnabled(WebApplication app) =>
        app.Environment.IsDevelopment()
        || app.Configuration.GetSection(ApiDocumentationOptions.SectionName).Get<ApiDocumentationOptions>()?.Enabled == true;
}
