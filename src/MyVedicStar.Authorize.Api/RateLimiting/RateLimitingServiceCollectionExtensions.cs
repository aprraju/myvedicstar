using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Api.Networking;

namespace MyVedicStar.Authorize.Api.RateLimiting;

internal static class RateLimitingServiceCollectionExtensions
{
    /// <summary>Name of the policy applied to API controllers.</summary>
    public const string ApiPolicy = "api";

    // Requests whose client address is unknown share one partition, so they cannot bypass the limit.
    private const string UnknownClientPartition = "unknown";

    /// <summary>
    /// Registers a fixed-window limit per client IP. Rejected requests get 429 with a <c>Retry-After</c> header and a
    /// Problem Details body (written by the status code pages middleware).
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiRateLimitingOptions>()
            .Bind(configuration.GetSection(ApiRateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<ApiRateLimitingOptions>>((limiter, rateLimiting) =>
            {
                var limits = rateLimiting.Value;

                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.AddPolicy(ApiPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                    context.GetClientIpAddress()?.ToString() ?? UnknownClientPartition,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = limits.Window,
                        QueueLimit = limits.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    }));

                limiter.OnRejected = (rejected, _) =>
                {
                    if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        rejected.HttpContext.Response.Headers.RetryAfter =
                            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                    }

                    return ValueTask.CompletedTask;
                };
            });

        return services;
    }
}
