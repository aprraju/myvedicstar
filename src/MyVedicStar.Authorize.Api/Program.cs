using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Api.Authentication;
using MyVedicStar.Authorize.Api.ErrorHandling;
using MyVedicStar.Authorize.Api.Health;
using MyVedicStar.Authorize.Api.Logging;
using MyVedicStar.Authorize.Api.Networking;
using MyVedicStar.Authorize.Api.Observability;
using MyVedicStar.Authorize.Api.OpenApi;
using MyVedicStar.Authorize.Api.RateLimiting;
using MyVedicStar.Authorize.Api.Security;
using MyVedicStar.Authorize.Business.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Do not advertise the server implementation.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

// Graceful shutdown: finish in-flight requests within the container's termination grace period.
builder.Services.Configure<HostOptions>(builder.Configuration.GetSection(nameof(HostOptions)));

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddApiDocumentation(builder.Configuration);
builder.Services.AddHealthEndpoints(builder.Configuration);
builder.Services.AddApiObservability(builder.Configuration);
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddTrustedForwardedHeaders(builder.Configuration);
builder.Services.AddEntraExternalIdAuthentication(builder.Configuration);
builder.Services.AddAuthorizeBusiness(builder.Configuration);

var app = builder.Build();

var healthEndpoints = app.Services.GetRequiredService<IOptions<HealthEndpointOptions>>().Value;

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
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
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapControllers().RequireRateLimiting(RateLimitingServiceCollectionExtensions.ApiPolicy);

app.Run();

public partial class Program;
