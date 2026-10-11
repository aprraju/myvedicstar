using Microsoft.Extensions.Configuration.Json;
using MyVedicDay.Api.Hosting;
using MyVedicStar.Authorize.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);
// API defaults precede the host's environment, command-line, and test configuration providers.
// Remove automatic root appsettings files: referenced executable projects can copy colliding filenames.
foreach (var source in builder.Configuration.Sources.OfType<JsonConfigurationSource>().ToArray())
{
    if (source.Path is "appsettings.json" || source.Path == $"appsettings.{builder.Environment.EnvironmentName}.json")
        builder.Configuration.Sources.Remove(source);
}
var apiDefaults = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("apis/star/appsettings.json", optional: false)
    .AddJsonFile("apis/day/appsettings.json", optional: false)
    .AddJsonFile($"apis/day/appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .Build();
builder.Configuration.Sources.Insert(0, new ChainedConfigurationSource
{
    Configuration = apiDefaults,
    ShouldDisposeConfiguration = true,
});
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
// Star handles its domain exceptions; all other failures use the same shared infrastructure.
builder.Services.AddMyVedicStarModule(builder.Configuration);
builder.Services.AddMyVedicDayApi(builder.Configuration);
var app = builder.Build();
app.UseMyVedicDayApi();
app.Run();

public partial class Program;