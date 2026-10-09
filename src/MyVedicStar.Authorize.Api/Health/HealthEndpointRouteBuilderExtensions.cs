using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MyVedicStar.ApiProtection.Configuration;
using MyVedicStar.Shared.Health;

namespace MyVedicStar.Authorize.Api.Health;

internal static class HealthEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Registers the health check service and the endpoint paths from configuration. Startup fails if a path is not
    /// listed in <c>ApiProtection:HealthCheckPaths</c>, because unlisted paths would be rate limited and probes could fail.
    /// </summary>
    public static IServiceCollection AddHealthEndpoints(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HealthEndpointOptions>()
            .Bind(configuration.GetSection(HealthEndpointOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate<IOptions<ApiProtectionOptions>>(
                (health, protection) => IsRateLimitExempt(health.LivenessPath, protection.Value)
                    && IsRateLimitExempt(health.ReadinessPath, protection.Value),
                $"{HealthEndpointOptions.SectionName} paths must also be listed in {ApiProtectionOptions.SectionName}:{nameof(ApiProtectionOptions.HealthCheckPaths)}.")
            .ValidateOnStart();

        services.AddHealthChecks();

        return services;
    }

    private static bool IsRateLimitExempt(string path, ApiProtectionOptions protection) =>
        protection.HealthCheckPaths.Any(exempt => string.Equals(exempt.TrimEnd('/'), path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Maps anonymous liveness and readiness endpoints for container probes. Responses contain only the overall status.
    /// Neither endpoint is rate limited, so probes are never rejected.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<HealthEndpointOptions>>().Value;

        endpoints.MapHealthChecks(options.LivenessPath, new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        endpoints.MapHealthChecks(options.ReadinessPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthCheckTags.Readiness) })
            .AllowAnonymous();

        return endpoints;
    }
}
