namespace MyVedicStar.Authorize.Api.Security;

/// <summary>
/// Adds OWASP-recommended response headers to every response. They are fixed security behaviour, not configuration.
/// HSTS is added separately by the HSTS middleware.
/// </summary>
/// <remarks>
/// The headers are written when the response starts rather than when the request arrives: the exception handler
/// clears the response (headers included) before writing Problem Details, so headers set earlier would be lost on
/// every error response.
/// </remarks>
internal sealed class SecurityHeadersMiddleware
{
    private static readonly Func<object, Task> AddSecurityHeaders = state =>
    {
        var headers = ((HttpResponse)state).Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        return Task.CompletedTask;
    };

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(AddSecurityHeaders, context.Response);
        return _next(context);
    }
}
