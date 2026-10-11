using MyVedicPlatform.Api;
using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using MyVedicDay.Api.Infrastructure.ErrorHandling;
using MyVedicDay.Api.Infrastructure.Observability;
using MyVedicDay.Api.Infrastructure.OpenApi;
using MyVedicDay.Panchang.Business.Diagnostics;
using MyVedicStar.ApiProtection.Auth.Extensions;
using MyVedicStar.ApiProtection.Configuration;
using MyVedicStar.ApiProtection.Extensions;
using MyVedicStar.ApiProtection.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MyVedicDay.Api.Infrastructure;

/// <summary>
/// Registers API-layer concerns: MVC, OpenAPI, Problem Details, CORS, health checks and telemetry, plus the shared
/// MyVedicStar.ApiProtection edge protection (trusted proxies, named sliding-window rate limits, request size).
/// </summary>
internal static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Strict number handling keeps numbers as JSON numbers (and documents them as such in OpenAPI).
        services.AddControllers()
            .AddApplicationPart(typeof(ApiServiceCollectionExtensions).Assembly)
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            });
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        services.AddApiDocumentation(configuration);
        services.AddOptions<HealthCheckEndpointOptions>()
            .Bind(configuration.GetSection(HealthCheckEndpointOptions.SectionName));
        services.AddHealthChecks();

        // Edge protection from the shared library (ADR-0014). It sets body limits per request only for POST/PUT/PATCH,
        // so Kestrel's default for every other request uses the same configured limit.
        services.AddMyVedicStarApiProtection(configuration);
        services.AddMyVedicStarAuthentication(configuration);
        services.AddOptions<KestrelServerOptions>()
            .Configure<IOptions<ApiProtectionOptions>>((kestrel, protection) =>
                kestrel.Limits.MaxRequestBodySize = protection.Value.RequestSize.StandardPostBytes);

        services.AddCommonProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddApiCors(configuration);
        services.AddApiTelemetry(configuration);

        return services;
    }

    private static void AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection(ApiCorsOptions.SectionName).Get<ApiCorsOptions>()?.AllowedOrigins ?? [];

        services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins).WithMethods(HttpMethods.Get).AllowAnyHeader().WithExposedHeaders("X-Correlation-ID");
            }
        }));
    }

    private static void AddApiTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ObservabilityOptions.SectionName);
        services.AddOptions<ObservabilityOptions>().Bind(section).ValidateDataAnnotations().ValidateOnStart();
        var observability = section.Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                observability.ServiceName,
                serviceVersion: typeof(ApiServiceCollectionExtensions).Assembly.GetName().Version?.ToString()))
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddMeter(PanchangTelemetry.MeterName, ProtectionMetrics.MeterName, MyVedicStar.Caching.ApplicationCacheMetrics.MeterName, "Npgsql"));

        if (observability.OtlpEndpoint is not null)
        {
            telemetry.UseOtlpExporter(OpenTelemetry.Exporter.OtlpExportProtocol.Grpc, observability.OtlpEndpoint);
        }

        if (!string.IsNullOrWhiteSpace(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            telemetry.UseAzureMonitor();
        }
    }
}
