using ProxyEdu.Server.Hubs;
using ProxyEdu.Server.Security;
using ProxyEdu.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<StudentManagerService>();
builder.Services.AddSingleton<FilterService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<ProxyServerService>();
builder.Services.AddSingleton<ServerHealthService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProxyServerService>());
builder.Services.AddHostedService<DiscoveryService>();

// Run as Windows Service in production
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "ProxyEdu Server";
});

var app = builder.Build();

app.UseMiddleware<BasicAuthMiddleware>();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.MapControllers();
app.MapHub<ProxyHub>("/hub");

// Serve dashboard on all routes (SPA fallback)
app.MapFallbackToFile("index.html");

app.Run("http://0.0.0.0:5000");
