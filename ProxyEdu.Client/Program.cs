using ProxyEdu.Client.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configuração do serviço de proteção
builder.Services.AddSingleton<ServerEndpointResolver>();
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
