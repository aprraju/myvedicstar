using Azure.Monitor.OpenTelemetry.AspNetCore;
using MyVedicStar.Authorize.Api.Health;
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
        var serviceVersion = typeof(ObservabilityServiceCollectionExtensions).Assembly.GetName().Version?.ToString();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName ?? string.Empty, serviceVersion: serviceVersion))
            .UseAzureMonitor();

        // Container probes hit the health endpoints every few seconds; they are not useful request telemetry.
        services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
            options.Filter = context => !context.IsHealthRequest());

        return services;
    }
}
