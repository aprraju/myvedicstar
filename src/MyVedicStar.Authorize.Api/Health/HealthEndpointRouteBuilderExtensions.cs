using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MyVedicStar.Shared.Health;

namespace MyVedicStar.Authorize.Api.Health;

internal static class HealthEndpointRouteBuilderExtensions
{
    /// <summary>Registers the health check service and the endpoint paths from configuration.</summary>
    public static IServiceCollection AddHealthEndpoints(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HealthEndpointOptions>()
            .Bind(configuration.GetSection(HealthEndpointOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHealthChecks();

        return services;
    }

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
