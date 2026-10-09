namespace MyVedicStar.Authorize.Api.Security;

/// <summary>
/// Adds OWASP-recommended response headers to every response. They are fixed security behaviour, not configuration.
/// HSTS is added separately by the HSTS middleware.
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";

        return _next(context);
    }
}
