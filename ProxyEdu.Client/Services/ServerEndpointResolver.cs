using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ProxyEdu.Client.Services;

public sealed class ServerEndpoint
{
    public string Ip { get; init; } = "";
    public int DashboardPort { get; init; }
    public int ProxyPort { get; init; }
    public bool EnableHttpsInspection { get; init; }
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
                    ProxyPort = defaultProxyPort,
                    EnableHttpsInspection = _config.GetValue<bool?>("Server:EnableHttpsInspection") ?? false
                };
                return _cachedEndpoint;
            }

            if (!autoDiscover)
            {
                throw new InvalidOperationException("Server:Ip vazio e auto descoberta desabilitada.");
            }

            var configuredFallback = await TryConfiguredFallbacksAsync(
                defaultDashboardPort,
                defaultProxyPort,
                cancellationToken);
            if (configuredFallback is not null)
            {
                _cachedEndpoint = configuredFallback;
                return _cachedEndpoint;
            }

            var discovered = await DiscoverAsync(defaultDashboardPort, defaultProxyPort, cancellationToken);
            discovered ??= await TryLocalServerAsync(defaultDashboardPort, defaultProxyPort, cancellationToken);
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
        var targets = BuildDiscoveryTargets(discoveryPort);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var target in targets)
                {
                    await udp.SendAsync(probe, probe.Length, target);
                }

                var receiveTask = udp.ReceiveAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                var completed = await Task.WhenAny(receiveTask, timeoutTask);

                if (completed != receiveTask)
                {
                    continue;
                }

                var response = await receiveTask;
                var dashboardPort = defaultDashboardPort;
                var proxyPort = defaultProxyPort;
                var enableHttpsInspection = false;

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
                    if (json.RootElement.TryGetProperty("enableHttpsInspection", out var httpsInspectionProp) &&
                        (httpsInspectionProp.ValueKind == JsonValueKind.True || httpsInspectionProp.ValueKind == JsonValueKind.False))
                    {
                        enableHttpsInspection = httpsInspectionProp.GetBoolean();
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
                    ProxyPort = proxyPort,
                    EnableHttpsInspection = enableHttpsInspection
                };
            }
            catch (SocketException ex) when (
                ex.SocketErrorCode == SocketError.HostUnreachable ||
                ex.SocketErrorCode == SocketError.NetworkUnreachable ||
                ex.SocketErrorCode == SocketError.AddressNotAvailable ||
                ex.SocketErrorCode == SocketError.AddressFamilyNotSupported ||
                ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                _logger.LogWarning(
                    "Discovery UDP indisponivel nesta rede (tentativa {Attempt}/3): {SocketError}",
                    attempt,
                    ex.SocketErrorCode);
            }
        }

        return null;
    }

    private async Task<ServerEndpoint?> TryConfiguredFallbacksAsync(
        int defaultDashboardPort,
        int defaultProxyPort,
        CancellationToken cancellationToken)
    {
        var fallbackIps = (_config["Server:FallbackIps"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var ip in fallbackIps)
        {
            var endpoint = await TryHttpEndpointAsync(ip, defaultDashboardPort, defaultProxyPort, cancellationToken);
            if (endpoint is not null)
            {
                return endpoint;
            }
        }

        return null;
    }

    private Task<ServerEndpoint?> TryLocalServerAsync(
        int defaultDashboardPort,
        int defaultProxyPort,
        CancellationToken cancellationToken)
    {
        return TryHttpEndpointAsync("127.0.0.1", defaultDashboardPort, defaultProxyPort, cancellationToken);
    }

    private async Task<ServerEndpoint?> TryHttpEndpointAsync(
        string ip,
        int defaultDashboardPort,
        int defaultProxyPort,
        CancellationToken cancellationToken)
    {
        try
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            using var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(800)
            };

            using var response = await http.GetAsync($"http://{ip}:{defaultDashboardPort}/", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            _logger.LogInformation("Servidor ProxyEdu localizado por fallback HTTP em {Ip}:{Port}", ip, defaultDashboardPort);
            return new ServerEndpoint
            {
                Ip = ip,
                DashboardPort = defaultDashboardPort,
                ProxyPort = defaultProxyPort,
                EnableHttpsInspection = _config.GetValue<bool?>("Server:EnableHttpsInspection") ?? false
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null;
        }
    }

    private static IReadOnlyList<IPEndPoint> BuildDiscoveryTargets(int discoveryPort)
    {
        var targets = new Dictionary<string, IPEndPoint>(StringComparer.OrdinalIgnoreCase)
        {
            [IPAddress.Broadcast.ToString()] = new(IPAddress.Broadcast, discoveryPort),
            [IPAddress.Loopback.ToString()] = new(IPAddress.Loopback, discoveryPort)
        };

        foreach (var address in GetInterfaceBroadcastAddresses())
        {
            targets.TryAdd(address.ToString(), new IPEndPoint(address, discoveryPort));
        }

        return targets.Values.ToList();
    }

    private static IEnumerable<IPAddress> GetInterfaceBroadcastAddresses()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                    unicast.IPv4Mask is null)
                {
                    continue;
                }

                var ipBytes = unicast.Address.GetAddressBytes();
                var maskBytes = unicast.IPv4Mask.GetAddressBytes();
                var broadcastBytes = new byte[ipBytes.Length];

                for (var i = 0; i < ipBytes.Length; i++)
                {
                    broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                }

                yield return new IPAddress(broadcastBytes);
            }
        }
    }
}
