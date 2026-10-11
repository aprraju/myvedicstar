using System.Diagnostics;

namespace MyVedicPlatform.Api;

/// <summary>Shared error metadata and correlation preservation for both standalone APIs and the combined host.</summary>
internal static class ProblemDetailsPolicy
{
    internal const string CorrelationItem = "MyVedicPlatform.CorrelationId";
    internal const string DependencyTitle = "A dependency is temporarily unavailable. Please retry later.";
    internal const string UnexpectedTitle = "An unexpected error occurred.";

    public static IServiceCollection AddCommonProblemDetails(this IServiceCollection services) => services.AddProblemDetails(options =>
        options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
            var correlationId = context.HttpContext.Items[CorrelationItem] as string
                ?? context.HttpContext.Response.Headers["X-Correlation-ID"].ToString();
            if (correlationId.Length > 0)
                context.ProblemDetails.Extensions["correlationId"] = correlationId;
        });
}

/// <summary>Retains the protection library's validated correlation ID when exception handling clears the response.</summary>
internal sealed class CorrelationResponseMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Response.Headers["X-Correlation-ID"].ToString();
        if (correlationId.Length > 0)
        {
            context.Items[ProblemDetailsPolicy.CorrelationItem] = correlationId;
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-Correlation-ID"] = correlationId;
                return Task.CompletedTask;
            });
        }
        await next(context);
    }
}