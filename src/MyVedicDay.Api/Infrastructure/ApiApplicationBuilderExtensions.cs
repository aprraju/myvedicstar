using MyVedicPlatform.Api;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MyVedicDay.Api.Infrastructure.Observability;
using MyVedicDay.Api.Infrastructure.OpenApi;
using MyVedicDay.Api.Infrastructure.Security;
using MyVedicStar.ApiProtection.Extensions;
using MyVedicStar.ApiProtection.Auth;

namespace MyVedicDay.Api.Infrastructure;

/// <summary>
/// Configures the HTTP pipeline and endpoints.
/// </summary>
internal static class ApiApplicationBuilderExtensions
{
    public static WebApplication UseApiInfrastructure(this WebApplication app)
    {
        // First, so the client IP (rate limits, logs) and scheme (HTTPS redirection) are those forwarded by the trusted
        // proxies in ApiProtection:TrustedProxies; with none configured nothing is forwarded.
        app.UseForwardedHeaders();

        app.UseRouting();
        app.UseMyVedicStarApiProtection();
        app.UseMiddleware<CorrelationResponseMiddleware>();
        // Logging and error handling run inside the library's correlation scope.
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        if (app.Configuration.GetValue("TransportSecurity:RedirectHttpToHttps", true))
            app.UseHttpsRedirection();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseCors();


        // Request size (needs endpoint metadata, so after routing) and correlation. The rate limiter comes after
        // authentication so user-based partitions resolve correctly.
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        // Documentation middleware also runs after authentication and fallback authorization.
        app.MapApiDocumentation();

        app.MapControllers().RequireAuthorization(AuthPolicyNames.ApiScope);

        // Liveness: the process is up. Readiness: dependencies (distributed cache) respond.
        // Paths come from HealthChecks:LivenessPath / HealthChecks:ReadinessPath (appsettings.json) — the same values
        // that deployment/update.ps1 reads to configure Container App health probes (single source of truth).
        // Both paths are also listed in ApiProtection:HealthCheckPaths so no rate limit ever applies to the probes.
        var hcPaths = app.Services.GetRequiredService<IOptions<HealthCheckEndpointOptions>>().Value;
        app.MapHealthChecks(hcPaths.LivenessPath,  new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks(hcPaths.ReadinessPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

        return app;
    }
}
