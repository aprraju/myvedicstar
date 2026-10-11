using MyVedicDay.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMyVedicDayApi(builder.Configuration);

var app = builder.Build();

app.UseMyVedicDayApi();

app.Run();
