using DaliAPI;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers(options =>
{
    options.Filters.Add<HttpResponseExceptionFilter>();
});

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "DALI API Service";
});

//builder.Services.AddHttpLogging(o => { });

//builder.Services.window

var app = builder.Build();

//app.UseHttpLogging();

// Configure the HTTP request pipeline.

app.UseAuthorization();

app.MapControllers();

app.UseBlazorFrameworkFiles();

app.UseStaticFiles();

app.MapFallbackToFile("index.html");

app.Run();