using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ProxyEdu.Server.Services;

public class DiscoveryService : BackgroundService
{
    private const string DiscoveryProbe = "PROXYEDU_DISCOVER_V1";
    private readonly ILogger<DiscoveryService> _logger;
    private readonly IConfiguration _config;
    private readonly DatabaseService _db;
    private UdpClient? _udpClient;

    public DiscoveryService(
        ILogger<DiscoveryService> logger,
        IConfiguration config,
        DatabaseService db)
    {
        _logger = logger;
        _config = config;
        _db = db;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var discoveryPort = _config.GetValue<int?>("Discovery:Port") ?? 50505;
        _udpClient = new UdpClient(discoveryPort) { EnableBroadcast = true };

        _logger.LogInformation("Discovery service escutando na porta UDP {Port}", discoveryPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var packet = await _udpClient.ReceiveAsync(stoppingToken);
                var payload = Encoding.UTF8.GetString(packet.Buffer).Trim();
                if (!string.Equals(payload, DiscoveryProbe, StringComparison.Ordinal))
                {
                    continue;
                }

                var settings = _db.GetSettings();
                var response = JsonSerializer.Serialize(new
                {
                    protocol = "discovery-v1",
                    dashboardPort = settings.DashboardPort,
                    proxyPort = settings.ProxyPort,
                    serverName = Environment.MachineName
                });

                var responseBuffer = Encoding.UTF8.GetBytes(response);
                await _udpClient.SendAsync(responseBuffer, responseBuffer.Length, packet.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha no loop de discovery");
            }
        }
    }

    public override void Dispose()
    {
        _udpClient?.Dispose();
        base.Dispose();
    }
}
