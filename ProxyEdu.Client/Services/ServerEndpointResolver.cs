using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ProxyEdu.Client.Services;

public sealed class ServerEndpoint
{
    public string Ip { get; init; } = "";
    public int DashboardPort { get; init; }
    public int ProxyPort { get; init; }
}

public class ServerEndpointResolver
{
    private const string DiscoveryProbe = "PROXYEDU_DISCOVER_V1";
    private readonly IConfiguration _config;
    private readonly ILogger<ServerEndpointResolver> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ServerEndpoint? _cachedEndpoint;

    public ServerEndpointResolver(
        IConfiguration config,
        ILogger<ServerEndpointResolver> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<ServerEndpoint> ResolveAsync(CancellationToken cancellationToken)
    {
        if (_cachedEndpoint is not null)
        {
            return _cachedEndpoint;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedEndpoint is not null)
            {
                return _cachedEndpoint;
            }

            var configuredIp = (_config["Server:Ip"] ?? "").Trim();
            var defaultDashboardPort = int.Parse(_config["Server:DashboardPort"] ?? "5000");
            var defaultProxyPort = int.Parse(_config["Server:ProxyPort"] ?? "8888");
            var autoDiscover = _config.GetValue<bool?>("Server:AutoDiscover") ?? true;

            if (!string.IsNullOrWhiteSpace(configuredIp))
            {
                _cachedEndpoint = new ServerEndpoint
                {
                    Ip = configuredIp,
                    DashboardPort = defaultDashboardPort,
                    ProxyPort = defaultProxyPort
                };
                return _cachedEndpoint;
            }

            if (!autoDiscover)
            {
                throw new InvalidOperationException("Server:Ip vazio e auto descoberta desabilitada.");
            }

            var discovered = await DiscoverAsync(defaultDashboardPort, defaultProxyPort, cancellationToken);
            if (discovered is null)
            {
                throw new InvalidOperationException("Nenhum servidor ProxyEdu encontrado na rede local.");
            }

            _cachedEndpoint = discovered;
            return _cachedEndpoint;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate()
    {
        _cachedEndpoint = null;
    }

    private async Task<ServerEndpoint?> DiscoverAsync(
        int defaultDashboardPort,
        int defaultProxyPort,
        CancellationToken cancellationToken)
    {
        var discoveryPort = _config.GetValue<int?>("Server:DiscoveryPort") ?? 50505;
        using var udp = new UdpClient(0) { EnableBroadcast = true };
        var probe = Encoding.UTF8.GetBytes(DiscoveryProbe);
        var broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, discoveryPort);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await udp.SendAsync(probe, probe.Length, broadcastEndPoint);

                var receiveTask = udp.ReceiveAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                var completed = await Task.WhenAny(receiveTask, timeoutTask);

                if (completed != receiveTask)
                {
                    continue;
                }

                var response = receiveTask.Result;
                var dashboardPort = defaultDashboardPort;
                var proxyPort = defaultProxyPort;

                try
                {
                    using var json = JsonDocument.Parse(response.Buffer);
                    if (json.RootElement.TryGetProperty("dashboardPort", out var dashProp) &&
                        dashProp.TryGetInt32(out var dashPortParsed))
                    {
                        dashboardPort = dashPortParsed;
                    }
                    if (json.RootElement.TryGetProperty("proxyPort", out var proxyProp) &&
                        proxyProp.TryGetInt32(out var proxyPortParsed))
                    {
                        proxyPort = proxyPortParsed;
                    }
                }
                catch
                {
                    // Ignore invalid payload and use defaults.
                }

                var ip = response.RemoteEndPoint.Address.ToString();
                _logger.LogInformation("Servidor descoberto automaticamente em {Ip}:{Port}", ip, dashboardPort);
                return new ServerEndpoint
                {
                    Ip = ip,
                    DashboardPort = dashboardPort,
                    ProxyPort = proxyPort
                };
            }
            catch (SocketException ex) when (
                ex.SocketErrorCode == SocketError.HostUnreachable ||
                ex.SocketErrorCode == SocketError.NetworkUnreachable ||
                ex.SocketErrorCode == SocketError.AddressNotAvailable ||
                ex.SocketErrorCode == SocketError.AddressFamilyNotSupported)
            {
                _logger.LogWarning(
                    "Discovery UDP indisponivel nesta rede (tentativa {Attempt}/3): {SocketError}",
                    attempt,
                    ex.SocketErrorCode);
            }
        }

        return null;
    }
}
