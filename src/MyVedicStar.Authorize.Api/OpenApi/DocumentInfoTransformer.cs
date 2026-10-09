using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using MyVedicStar.Authorize.Api.Options;

namespace MyVedicStar.Authorize.Api.OpenApi;

/// <summary>Sets the OpenAPI document's title and description from configuration.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    private readonly ApiDocumentationOptions _options;

    public DocumentInfoTransformer(IOptions<ApiDocumentationOptions> options)
    {
        _options = options.Value;
    }

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info ??= new OpenApiInfo();
        document.Info.Title = _options.Title;

        if (!string.IsNullOrWhiteSpace(_options.Description))
        {
            document.Info.Description = _options.Description;
        }

        return Task.CompletedTask;
    }
}
