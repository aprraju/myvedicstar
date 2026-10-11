using MyVedicStar.API.Authorization.ErrorHandling;
using MyVedicStar.Authorize.Business.DependencyInjection;
using Microsoft.Extensions.Options;
using MyVedicStar.ApiProtection.Configuration;
using MyVedicStar.API.Authorization.Health;
using MyVedicStar.API.Authorization.Options;
using MyVedicStar.API.Authorization.OpenApi;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace MyVedicStar.API.Hosting;

public static class MyVedicStarApiModule
{
    /// <summary>Adds Star endpoints and business services to a host that already owns the shared API infrastructure.</summary>
    public static IServiceCollection AddMyVedicStarModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers().AddApplicationPart(typeof(MyVedicStarApiModule).Assembly)
            .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new GeoNamesCompatibilityControllerFilter()));
        services.Configure<HostOptions>(configuration.GetSection(nameof(HostOptions)));
        services.AddAuthorizeBusiness(configuration);
        services.AddOptions<HealthEndpointOptions>()
            .Bind(configuration.GetSection(HealthEndpointOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate<IOptions<ApiProtectionOptions>>((health, protection) =>
                new[] { health.LivenessPath, health.ReadinessPath }.All(path =>
                    protection.Value.HealthCheckPaths.Any(exempt => string.Equals(exempt.TrimEnd('/'), path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))),
                "HealthChecks paths must also be listed in ApiProtection:HealthCheckPaths.")
            .ValidateOnStart();
        services.Configure<ApiDocumentationOptions>(configuration.GetSection(ApiDocumentationOptions.SectionName));
        services.Configure<Microsoft.AspNetCore.OpenApi.OpenApiOptions>("v1", options =>
            options.AddDocumentTransformer<HttpsServerUrlTransformer>());
        services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(MyVedicStar.Shared.Caching.CacheMetrics.MeterName, MyVedicStar.Caching.ApplicationCacheMetrics.MeterName, "Npgsql"));
        services.AddSingleton<GlobalExceptionHandler>();
        services.AddExceptionHandler<StarDomainExceptionHandler>();
        return services;
    }

    private sealed class StarDomainExceptionHandler(GlobalExceptionHandler handler) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
    {
        public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken) =>
            exception is MyVedicStar.Shared.Exceptions.MyVedicStarException
                ? handler.TryHandleAsync(context, exception, cancellationToken)
                : ValueTask.FromResult(false);
    }

    private sealed class GeoNamesCompatibilityControllerFilter : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var compatibilityController = feature.Controllers.FirstOrDefault(type =>
                type.Assembly == typeof(GeoNames.ILocationSearchProvider).Assembly && type.Name == "GeoNamesLocationsController");
            if (compatibilityController is not null) feature.Controllers.Remove(compatibilityController);
        }
    }

}
