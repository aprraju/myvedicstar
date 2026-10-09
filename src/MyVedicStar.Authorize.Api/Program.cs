using Microsoft.Extensions.Options;
using MyVedicStar.ApiProtection.Extensions;
using MyVedicStar.Authorize.Api.Authentication;
using MyVedicStar.Authorize.Api.Authorization;
using MyVedicStar.Authorize.Api.ErrorHandling;
using MyVedicStar.Authorize.Api.Health;
using MyVedicStar.Authorize.Api.Logging;
using MyVedicStar.Authorize.Api.Observability;
using MyVedicStar.Authorize.Api.OpenApi;
using MyVedicStar.Authorize.Api.Security;
using MyVedicStar.Authorize.Business.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Do not advertise the server implementation.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

// Graceful shutdown: finish in-flight requests within the container's termination grace period.
builder.Services.Configure<HostOptions>(builder.Configuration.GetSection(nameof(HostOptions)));

builder.Services.AddControllers(mvc => mvc.Conventions.Add(new DenyAnonymousControllersConvention()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Rate limiting, request-size limits, correlation IDs, trusted proxies and SSRF protection (ApiProtection section).
builder.Services.AddMyVedicStarApiProtection(builder.Configuration);
builder.Services.AddApiDocumentation(builder.Configuration);
builder.Services.AddHealthEndpoints(builder.Configuration);
builder.Services.AddApiObservability(builder.Configuration);
builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddApiAuthorization(builder.Configuration);
builder.Services.AddAuthorizeBusiness(builder.Configuration);

var app = builder.Build();

var healthEndpoints = app.Services.GetRequiredService<IOptions<HealthEndpointOptions>>().Value;

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
// Routing first: the protection middleware picks request-size limits from endpoint metadata.
app.UseRouting();
app.UseMyVedicStarApiProtection();
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
app.MapControllers().RequireAuthorization(AuthorizationPolicies.SignedInUser);

app.Run();

public partial class Program;
