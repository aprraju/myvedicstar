using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Api.Authentication;
using MyVedicStar.Authorize.Api.ErrorHandling;
using MyVedicStar.Authorize.Api.Health;
using MyVedicStar.Authorize.Api.Logging;
using MyVedicStar.Authorize.Api.Networking;
using MyVedicStar.Authorize.Api.Observability;
using MyVedicStar.Authorize.Api.OpenApi;
using MyVedicStar.Authorize.Api.Options;
using MyVedicStar.Authorize.Business.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ApiDocumentationOptions>()
    .Bind(builder.Configuration.GetSection(ApiDocumentationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Graceful shutdown: finish in-flight requests within the container's termination grace period.
builder.Services.Configure<HostOptions>(builder.Configuration.GetSection(nameof(HostOptions)));

builder.Services.AddControllers();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services.AddApiObservability(builder.Configuration);
builder.Services.AddTrustedForwardedHeaders(builder.Configuration);
builder.Services.AddEntraExternalIdAuthentication(builder.Configuration);
builder.Services.AddAuthorizeBusiness(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseWhen(context => !context.IsHealthRequest(), branch => branch.UseMiddleware<RequestLoggingMiddleware>());
app.UseExceptionHandler();
app.UseStatusCodePages();

var apiDocumentation = app.Services.GetRequiredService<IOptions<ApiDocumentationOptions>>().Value;
if (apiDocumentation.Enabled)
{
    app.MapOpenApi(apiDocumentation.OpenApiRoutePattern);
    app.UseSwaggerUI(options => options.SwaggerEndpoint(apiDocumentation.SwaggerEndpointUrl, apiDocumentation.Title));
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthEndpoints();
app.MapControllers();

app.Run();

public partial class Program;
