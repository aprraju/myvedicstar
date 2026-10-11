using System.Reflection;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Api.Health;
using MyVedicStar.Shared.Caching;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Resources;

namespace MyVedicStar.Authorize.Api.Observability;

internal static class ObservabilityServiceCollectionExtensions
{
    /// <summary>Standard configuration key read by the Azure Monitor exporter.</summary>
    public const string ApplicationInsightsConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>
    /// Exports traces, metrics and logs to Application Insights through OpenTelemetry when a connection string is configured.
    /// Without one (local development, tests) no exporter is registered and logs go to the console only.
    /// </summary>
    public static IServiceCollection AddApiObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ObservabilityOptions>()
            .Bind(configuration.GetSection(ObservabilityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (string.IsNullOrWhiteSpace(configuration[ApplicationInsightsConnectionStringKey]))
        {
            return services;
        }

        var serviceName = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()?.ServiceName;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName ?? string.Empty, serviceVersion: GetServiceVersion()))
            .WithMetrics(metrics => metrics.AddMeter(CacheMetrics.MeterName))
            .UseAzureMonitor();

        // Container probes hit the health endpoints every few seconds; they are not useful request telemetry.
        services.AddOptions<AspNetCoreTraceInstrumentationOptions>()
            .Configure<IOptions<HealthEndpointOptions>>((tracing, health) =>
                tracing.Filter = context => !health.Value.IsHealthRequest(context.Request.Path));

        return services;
    }

    /// <summary>The build's informational version, which includes the source commit (for example <c>1.0.0+1a2b3c4d</c>).</summary>
    private static string? GetServiceVersion()
    {
        var assembly = typeof(ObservabilityServiceCollectionExtensions).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
    }
}
