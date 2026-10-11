using MyVedicDay.Api.Infrastructure.OpenApi;

namespace MyVedicDay.Api.Infrastructure.Security;

/// <summary>
/// Adds OWASP-recommended response headers for a JSON API.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    // Swagger UI is an HTML page that loads its own scripts and styles and uses inline bootstrap code.
    private const string SwaggerUiContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'";

    private static readonly PathString SwaggerUiPath = new("/" + ApiDocumentationExtensions.SwaggerUiPrefix);

    public Task InvokeAsync(HttpContext httpContext)
    {
        var contentSecurityPolicy = httpContext.Request.Path.StartsWithSegments(SwaggerUiPath)
            ? SwaggerUiContentSecurityPolicy
            : ApiContentSecurityPolicy;

        httpContext.Response.OnStarting(() =>
        {
            var headers = httpContext.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy = contentSecurityPolicy;
            headers["Cross-Origin-Resource-Policy"] = "same-site";
            // Deny all browser features this API never needs (OWASP recommended for public APIs).
            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=(), payment=(), usb=(), autoplay=()";
            return Task.CompletedTask;
        });

        return next(httpContext);
    }
}
