using MyVedicStar.Authorize.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);
builder.Services.AddMyVedicStarApi(builder.Configuration);
var app = builder.Build();
app.UseMyVedicStarApi();
app.Run();

public partial class Program;
