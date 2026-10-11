using MyVedicPlatform.Api;
using Microsoft.Extensions.Options;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.Auth.Authorization;
using MyVedicStar.ApiProtection.Auth.Extensions;
using MyVedicStar.ApiProtection.Extensions;
using MyVedicStar.Authorize.Api.ErrorHandling;
using MyVedicStar.Authorize.Api.Health;
using MyVedicStar.Authorize.Api.Logging;
using MyVedicStar.Authorize.Api.Observability;
using MyVedicStar.Authorize.Api.OpenApi;
using MyVedicStar.Authorize.Api.Security;
using MyVedicStar.Authorize.Business.DependencyInjection;

namespace MyVedicStar.Authorize.Api.Hosting;

public static class MyVedicStarApiModule
{
    /// <summary>Adds Star endpoints and business services to a host that already owns the shared API infrastructure.</summary>
    public static IServiceCollection AddMyVedicStarModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers().AddApplicationPart(typeof(MyVedicStarApiModule).Assembly);
        services.Configure<HostOptions>(configuration.GetSection(nameof(HostOptions)));
        services.AddAuthorizeBusiness(configuration);
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

    public static IServiceCollection AddMyVedicStarApi(this IServiceCollection services, IConfiguration configuration)
    {
        // Graceful shutdown: finish in-flight requests within the container's termination grace period.
        services.Configure<HostOptions>(configuration.GetSection(nameof(HostOptions)));

        // Startup fails if any controller allows anonymous access.
        services.AddControllers().AddApplicationPart(typeof(MyVedicStarApiModule).Assembly).DenyAnonymousControllers();
        services.AddCommonProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Rate limiting, request-size limits, correlation IDs, trusted proxies and SSRF protection (ApiProtection section).
        services.AddMyVedicStarApiProtection(configuration);
        services.AddApiDocumentation(configuration);
        services.AddHealthEndpoints(configuration);
        services.AddApiObservability(configuration);
        // Multi-scheme bearer authentication, the scope policy and secure-by-default authorization (Authentication and
        // Authorization sections).
        services.AddMyVedicStarAuthentication(configuration);
        services.AddAuthorizeBusiness(configuration);

        return services;
    }

    public static WebApplication UseMyVedicStarApi(this WebApplication app)
    {
        var healthEndpoints = app.Services.GetRequiredService<IOptions<HealthEndpointOptions>>().Value;

        app.UseForwardedHeaders();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        // Routing first: the protection middleware picks request-size limits from endpoint metadata.
        app.UseRouting();
        app.UseMyVedicStarApiProtection();
        app.UseMiddleware<CorrelationResponseMiddleware>();
        app.UseWhen(context => !healthEndpoints.IsHealthRequest(context.Request.Path), branch => branch.UseMiddleware<RequestLoggingMiddleware>());
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        var transportSecurity = app.Configuration.GetSection(TransportSecurityOptions.SectionName).Get<TransportSecurityOptions>() ?? new TransportSecurityOptions();
        if (transportSecurity.RedirectHttpToHttps)
        {
            app.UseHttpsRedirection();
        }

        app.UseApiDocumentation();
        app.UseAuthentication();
        // After authentication so authenticated callers are partitioned by user rather than by IP.
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapHealthEndpoints();
        // Every controller endpoint requires a signed-in user with the API scope, even one that forgets [Authorize].
        app.MapControllers().RequireAuthorization(AuthPolicyNames.ApiScope);

        return app;
    }
}
