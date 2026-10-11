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
        });

        return services;
    }

    /// <summary>
    /// Exposes the documentation in Development, or elsewhere when <c>ApiDocumentation:Enabled</c> is true.
    /// </summary>
    public static WebApplication MapApiDocumentation(this WebApplication app)
    {
        var enabled = app.Environment.IsDevelopment()
            || app.Configuration.GetSection(ApiDocumentationOptions.SectionName).Get<ApiDocumentationOptions>()?.Enabled == true;
        if (!enabled)
        {
            return app;
        }

        // Generating the document runs real calculations for its examples: rate-limited like the API everywhere but
        // Development, so a public Swagger cannot be used to load the service.
        var document = app.MapOpenApi().AllowAnonymous();
        if (app.Environment.IsDevelopment())
        {
            document.DisableRateLimiting();
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
}
