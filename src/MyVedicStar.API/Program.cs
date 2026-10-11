using Microsoft.Extensions.Configuration.Json;
using MyVedicDay.Api.Hosting;
using MyVedicStar.API.Hosting;

var builder = WebApplication.CreateBuilder(args);
foreach (var source in builder.Configuration.Sources.OfType<JsonConfigurationSource>().ToArray())
{
    if (source.Path is "appsettings.json" || source.Path == $"appsettings.{builder.Environment.EnvironmentName}.json")
        builder.Configuration.Sources.Remove(source);
}
using var starDefaults = (ConfigurationRoot)new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("apis/star/appsettings.json", optional: false)
    .Build();
var apiDefaults = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("apis/star/appsettings.json", optional: false)
    .AddJsonFile("apis/day/appsettings.json", optional: false)
    .AddJsonFile($"apis/day/appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    // Star owns GeoNames; its defaults override Day's compatibility defaults without changing Day.
    .AddInMemoryCollection(starDefaults.GetSection("GeoNames").AsEnumerable().Where(setting => setting.Value is not null))
    .Build();
builder.Configuration.Sources.Insert(0, new ChainedConfigurationSource
{
    Configuration = apiDefaults,
    ShouldDisposeConfiguration = true,
});
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
builder.Services.AddMyVedicStarModule(builder.Configuration);
builder.Services.AddMyVedicDayApi(builder.Configuration);
var app = builder.Build();
app.UseMyVedicDayApi();
app.Run();

public partial class Program;
