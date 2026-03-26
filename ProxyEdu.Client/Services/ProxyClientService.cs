using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace ProxyEdu.Client.Services;

public class ProxyClientService : BackgroundService
{
    private readonly ILogger<ProxyClientService> _logger;
    private readonly ServerEndpointResolver _endpointResolver;
    private HubConnection? _hubConnection;
    private string? _currentHubUrl;
    private string? _currentProxyAddress;
    private string? _trustedRootThumbprint;
    private bool _proxyEnabled;

    [DllImport("wininet.dll")]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    public ProxyClientService(
        ILogger<ProxyClientService> logger,
        ServerEndpointResolver endpointResolver)
    {
        _logger = logger;
        _endpointResolver = endpointResolver;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var endpoint = await _endpointResolver.ResolveAsync(stoppingToken);
                var proxyAddress = $"{endpoint.Ip}:{endpoint.ProxyPort}";

                await EnsureProxyRootCertificateTrustedAsync(endpoint, stoppingToken);
                await EnsureHubConnectionAsync(endpoint, stoppingToken);

                if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Disconnected)
                {
                    await _hubConnection.StartAsync(stoppingToken);
                    await RegisterWithServer(endpoint);
                }

                if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected)
                {
                    EnsureProxyEnabled(proxyAddress);
                }
                else
                {
                    DisableProxyFailOpen();
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Discovery falhou: {Message}", ex.Message);
                _endpointResolver.Invalidate();
                DisableProxyFailOpen();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("Servidor indisponivel: {Message}", ex.Message);
                _endpointResolver.Invalidate();
                DisableProxyFailOpen();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no cliente");
                _endpointResolver.Invalidate();
                DisableProxyFailOpen();
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }

        SetWindowsProxy("", false);
    }

    private void EnsureProxyEnabled(string proxyAddress)
    {
        if (_proxyEnabled && string.Equals(_currentProxyAddress, proxyAddress, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SetWindowsProxy(proxyAddress, true);
        _currentProxyAddress = proxyAddress;
        _proxyEnabled = true;
        _logger.LogInformation("Proxy configurado: {ProxyAddress}", proxyAddress);
    }

    private void DisableProxyFailOpen()
    {
        if (!_proxyEnabled && string.IsNullOrEmpty(_currentProxyAddress))
        {
            return;
        }

        SetWindowsProxy("", false);
        _proxyEnabled = false;
        _currentProxyAddress = null;
        _logger.LogWarning("Servidor offline: proxy desativado (modo fail-open, acesso liberado).");
    }

    private async Task EnsureHubConnectionAsync(ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        var hubUrl = $"http://{endpoint.Ip}:{endpoint.DashboardPort}/hub";
        if (string.Equals(_currentHubUrl, hubUrl, StringComparison.OrdinalIgnoreCase) && _hubConnection is not null)
        {
            return;
        }

        if (_hubConnection is not null)
        {
            await _hubConnection.StopAsync(cancellationToken);
            await _hubConnection.DisposeAsync();
        }

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On("Disconnect", () =>
        {
            _logger.LogWarning("Servidor solicitou desconexao");
        });

        _hubConnection.Reconnecting += ex =>
        {
            _logger.LogWarning("Conexao com servidor em reconexao: {Message}", ex?.Message);
            DisableProxyFailOpen();
            return Task.CompletedTask;
        };

        _hubConnection.Closed += ex =>
        {
            _logger.LogWarning("Conexao com servidor encerrada: {Message}", ex?.Message);
            _endpointResolver.Invalidate();
            DisableProxyFailOpen();
            return Task.CompletedTask;
        };

        _currentHubUrl = hubUrl;
    }

    private async Task RegisterWithServer(ServerEndpoint endpoint)
    {
        var studentName = Environment.UserName;
        var hostname = Environment.MachineName;
        var os = Environment.OSVersion.VersionString;
        var mac = GetMacAddress();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var payload = new
        {
            ip = GetLocalIp(),
            hostname,
            name = studentName,
            os,
            macAddress = mac,
            group = "default"
        };

        var response = await client.PostAsync(
            $"http://{endpoint.Ip}:{endpoint.DashboardPort}/api/students/register",
            new StringContent(JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Registrado no servidor como {Name}", studentName);
    }

    private async Task EnsureProxyRootCertificateTrustedAsync(ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        var certUrl = $"http://{endpoint.Ip}:{endpoint.DashboardPort}/api/certificate/root";

        using var handler = new HttpClientHandler { UseProxy = false };
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        using var response = await httpClient.GetAsync(certUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var certBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        using var rootCert = new X509Certificate2(certBytes);

        var thumbprint = rootCert.Thumbprint?.Replace(" ", "", StringComparison.Ordinal) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            throw new InvalidOperationException("Certificado raiz recebido sem thumbprint.");
        }

        if (string.Equals(_trustedRootThumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (CertificateExistsInRootStore(thumbprint, StoreLocation.LocalMachine) ||
            CertificateExistsInRootStore(thumbprint, StoreLocation.CurrentUser))
        {
            _trustedRootThumbprint = thumbprint;
            return;
        }

        var installed = InstallRootCertificate(rootCert, StoreLocation.LocalMachine) ||
                        InstallRootCertificate(rootCert, StoreLocation.CurrentUser);

        if (!installed)
        {
            throw new InvalidOperationException("Nao foi possivel instalar certificado raiz do proxy.");
        }

        _trustedRootThumbprint = thumbprint;
        _logger.LogInformation("Certificado raiz do proxy instalado: {Thumbprint}", thumbprint);
    }

    private static bool InstallRootCertificate(X509Certificate2 cert, StoreLocation storeLocation)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, storeLocation);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool CertificateExistsInRootStore(string thumbprint, StoreLocation storeLocation)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, storeLocation);
            store.Open(OpenFlags.ReadOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            return found.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SetWindowsProxy(string proxyAddress, bool enable)
    {
        ApplyProxyToAllLoadedUsers(proxyAddress, enable);
        ApplyProxyToCurrentUser(proxyAddress, enable);
        InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
    }

    private static void ApplyProxyToCurrentUser(string proxyAddress, bool enable)
    {
        using var regKey = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
        if (regKey is null)
        {
            return;
        }

        ApplyProxyValues(regKey, proxyAddress, enable);
    }

    private static void ApplyProxyToAllLoadedUsers(string proxyAddress, bool enable)
    {
        using var usersRoot = Registry.Users;
        foreach (var sid in usersRoot.GetSubKeyNames())
        {
            if (!LooksLikeUserSid(sid))
            {
                continue;
            }

            using var regKey = usersRoot.OpenSubKey(
                $@"{sid}\Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
            if (regKey is null)
            {
                continue;
            }

            ApplyProxyValues(regKey, proxyAddress, enable);
        }
    }

    private static bool LooksLikeUserSid(string sid)
    {
        return sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) ||
               sid.StartsWith("S-1-12-1-", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyProxyValues(RegistryKey regKey, string proxyAddress, bool enable)
    {
        if (enable)
        {
            regKey.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            regKey.SetValue("ProxyServer", proxyAddress, RegistryValueKind.String);
            regKey.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;192.168.*;<local>", RegistryValueKind.String);
            return;
        }

        regKey.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
    }

    private static string GetLocalIp()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            return host.AddressList
                .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string GetMacAddress()
    {
        try
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True");
            foreach (ManagementObject obj in searcher.Get())
            {
                return obj["MACAddress"]?.ToString() ?? "";
            }
        }
        catch
        {
        }

        return "";
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        SetWindowsProxy("", false);
        if (_hubConnection is not null)
        {
            await _hubConnection.StopAsync(cancellationToken);
            await _hubConnection.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
