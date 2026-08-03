using System.Net;
using ProxyEdu.Server.Hubs;
using ProxyEdu.Server.Security;
using ProxyEdu.Server.Services;
using ProxyEdu.Shared.Services;

// Helper to check if an IP address is in private network ranges 0
static bool IsPrivateNetwork(IPAddress ip)
{
    if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        return false;

    var bytes = ip.GetAddressBytes();
    // 10.x.x.x
    if (bytes[0] == 10) return true;
    // 172.16.x.x - 172.31.x.x
    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
    // 192.168.x.x
    if (bytes[0] == 192 && bytes[1] == 168) return true;
    // 169.254.x.x (link-local)
    if (bytes[0] == 169 && bytes[1] == 254) return true;

    return false;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin))
                    return true;

                try
                {
                    var uri = new Uri(origin);
                    var host = uri.Host;

                    if (host == "localhost" || host == "127.0.0.1")
                        return true;

                    if (IPAddress.TryParse(host, out var ip))
                    {
                        if (IsPrivateNetwork(ip))
                            return true;
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            })
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton(sp => new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
builder.Services.AddSingleton<UpdateService>();
builder.Services.AddSingleton<StudentUpdateBuffer>();
builder.Services.AddSingleton<StudentManagerService>();
builder.Services.AddSingleton<FilterService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<ProxyServerService>();
builder.Services.AddSingleton<ServerHealthService>();
builder.Services.AddSingleton<RuntimeDiagnostics>();
builder.Services.AddSingleton<OwnedTcpConnectionInspector>();
builder.Services.AddSingleton<ProxyHubConnectionRegistry>();
builder.Services.AddSingleton<LogQueueService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UpdateService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProxyServerService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<LogQueueService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<StudentUpdateBuffer>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<StudentManagerService>());
builder.Services.AddHostedService<DiscoveryService>();
builder.Services.AddHostedService<LogCleanupService>();

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
