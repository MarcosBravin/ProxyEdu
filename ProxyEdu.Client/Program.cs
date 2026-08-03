using ProxyEdu.Client.Services;
using ProxyEdu.Shared.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configuração do serviço de proteção 0
builder.Services.AddSingleton(sp => new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
builder.Services.AddSingleton<UpdateService>();
builder.Services.AddSingleton<ServerEndpointResolver>();
builder.Services.AddSingleton<ServerApiClient>();
builder.Services.AddSingleton<NetworkConnectivityMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UpdateService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<NetworkConnectivityMonitor>());
builder.Services.AddHostedService<ServiceProtectionService>();
builder.Services.AddHostedService<ProxyProtectionService>();
builder.Services.AddHostedService<ProxyClientService>();
builder.Services.AddHostedService<HeartbeatService>();

// Run as Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ProxyEdu Client";
});

var host = builder.Build();
host.Run();
