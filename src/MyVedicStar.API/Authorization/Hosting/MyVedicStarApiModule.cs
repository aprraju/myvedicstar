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
            .AddApplicationPart(typeof(MyVedicStar.API.Controllers.AuthController).Assembly)
            .AddApplicationPart(typeof(MyVedicStar.API.Controllers.SettingsController).Assembly)
            .AddApplicationPart(typeof(MyVedicStar.API.Controllers.EmailController).Assembly)
            .AddApplicationPart(typeof(MyVedicStar.API.Controllers.VedicProfilesController).Assembly)
            .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new GeoNamesCompatibilityControllerFilter()));
        services.Configure<HostOptions>(configuration.GetSection(nameof(HostOptions)));
        services.AddAuthorizeBusiness(configuration);
        MyVedicStar.Infrastructure.DependencyInjection.EmailModuleRegistration.AddEmailModule(services, configuration);
        services.AddRateLimiter(options => options.AddPolicy("email-communication", context =>
            System.Threading.RateLimiting.RateLimitPartition.GetSlidingWindowLimiter(
                context.RequestServices.GetRequiredService<MyVedicStar.ApiProtection.Abstractions.IClientIdentityResolver>().Resolve(context),
                _ => new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 5, Window = TimeSpan.FromMinutes(5), SegmentsPerWindow = 5, QueueLimit = 0,
                })));
        // The combined host supports public email POSTs; the standalone Day host keeps its existing CORS policy.
        services.PostConfigure<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>(options =>
        {
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            if (origins.Length > 0) options.AddDefaultPolicy(policy => policy.WithOrigins(origins)
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE").AllowAnyHeader().WithExposedHeaders("X-Correlation-ID"));
        });
        MyVedicStar.Settings.Business.SettingsBusinessRegistration.AddSettingsBusiness(services, configuration);
        MyVedicStar.Infrastructure.DependencyInjection.VedicProfilesModuleRegistration.AddVedicProfilesModule(services, configuration);
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
        // Npgsql's own ActivitySource makes each database command a dependency span, so login time can be split
        // between PostgreSQL and the rest of the request.
        services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(MyVedicStar.Shared.Caching.CacheMetrics.MeterName, MyVedicStar.Caching.ApplicationCacheMetrics.MeterName, "Npgsql"))
            .WithTracing(tracing => tracing.AddSource("Npgsql"));
        services.AddHostedService<TokenValidationWarmupService>();
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
                type.Assembly == typeof(GeoNames.GeoNamesLocationsController).Assembly && type.Name == "GeoNamesLocationsController");
            if (compatibilityController is not null) feature.Controllers.Remove(compatibilityController);
        }
    }

}
