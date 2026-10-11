using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using MyVedicStar.API.Authorization.Options;

namespace MyVedicStar.API.Authorization.OpenApi;

/// <summary>
/// Rewrites <c>http://</c> server URLs in the OpenAPI document to <c>https://</c> when
/// <see cref="ApiDocumentationOptions.HttpsServerUrlsOnly"/> is set, so Swagger UI's "Try it out" never calls the API
/// over plain HTTP even if the request reached the container without a trusted <c>X-Forwarded-Proto</c> header.
/// </summary>
internal sealed class HttpsServerUrlTransformer : IOpenApiDocumentTransformer
{
    private readonly ApiDocumentationOptions _options;

    public HttpsServerUrlTransformer(IOptions<ApiDocumentationOptions> options)
    {
        _options = options.Value;
    }

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (!_options.HttpsServerUrlsOnly || document.Servers is null)
        {
            return Task.CompletedTask;
        }

        foreach (var server in document.Servers)
        {
            if (server.Url is not null && Uri.TryCreate(server.Url, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttp)
            {
                // The default port is dropped: the ingress serves HTTPS on 443 whatever port the container listens on.
                server.Url = new UriBuilder(url) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri.ToString();
            }
        }

        return Task.CompletedTask;
    }
}
