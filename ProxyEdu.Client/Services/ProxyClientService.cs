using Microsoft.AspNetCore.SignalR.Client;
using ProxyEdu.Shared.Utils;
using System.Management;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace ProxyEdu.Client.Services;

public class ProxyClientService : BackgroundService
{
    private readonly ILogger<ProxyClientService> _logger;
    private readonly ServerEndpointResolver _endpointResolver;
    private readonly ServerApiClient _serverApi;
    private readonly NetworkConnectivityMonitor _networkMonitor;
    private readonly bool _failClosed;
    private HubConnection? _hubConnection;
    private string? _currentHubUrl;
    private string? _currentProxyAddress;
    private string? _trustedRootThumbprint;
    private string? _lastCertificateEndpoint;
    private DateTime _lastCertificateCheckUtc;
    private readonly string? _pinnedRootThumbprint;
    private bool _warnedUnpinnedCertificate;
    private bool _proxyEnabled;
    private bool _proxyAppliedThisRun;
    private int _needsRegistration = 1;
    private long _lastNetworkGeneration = -1;

    // Exponential backoff for retry
    private int _retryDelayMs = 2000;
    private const int MaxRetryDelayMs = 60000;
    private const int InitialRetryDelayMs = 2000;

    public ProxyClientService(
        ILogger<ProxyClientService> logger,
        ServerEndpointResolver endpointResolver,
        ServerApiClient serverApi,
        NetworkConnectivityMonitor networkMonitor,
        IConfiguration configuration)
    {
        _logger = logger;
        _endpointResolver = endpointResolver;
        _serverApi = serverApi;
        _networkMonitor = networkMonitor;
        _failClosed = configuration.GetValue<bool?>("Protection:FailClosed") ?? false;
        _pinnedRootThumbprint = NormalizeThumbprint(configuration["Server:RootCertificateThumbprint"]);
        var (existingProxy, existingEnabled) = WindowsProxyManager.GetCurrentProxySettings();
        _currentProxyAddress = existingProxy;
        _proxyEnabled = existingEnabled && !string.IsNullOrWhiteSpace(existingProxy);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var networkGeneration = _networkMonitor.Generation;
                if (networkGeneration != _lastNetworkGeneration)
                {
                    _lastNetworkGeneration = networkGeneration;
                    _endpointResolver.Invalidate();
                    await DisposeHubAsync(stoppingToken);
                    _logger.LogInformation("Network generation {Generation}; control connection will be recreated", networkGeneration);
                }
                var endpoint = await _endpointResolver.ResolveAsync(stoppingToken);
                var proxyAddress = $"{endpoint.Ip}:{endpoint.ProxyPort}";

                EnsureProxyEnabled(proxyAddress);
                await EnsureProxyRootCertificateTrustedAsync(endpoint, stoppingToken);
                await EnsureHubConnectionAsync(endpoint, stoppingToken);

                if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Disconnected)
                {
                    await _hubConnection.StartAsync(stoppingToken);
                    Interlocked.Exchange(ref _needsRegistration, 1);
                }

                if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected)
                {
                    if (Volatile.Read(ref _needsRegistration) == 1)
                    {
                        await RegisterWithServer(endpoint, stoppingToken);
                        Interlocked.Exchange(ref _needsRegistration, 0);
                    }
                    EnsureProxyEnabled(proxyAddress);
                    // Reset backoff on successful connection
                    _retryDelayMs = InitialRetryDelayMs;
                }

                else if (networkGeneration != _networkMonitor.Generation)
                {
                    _logger.LogInformation("Rede mudou durante a conexão; reconstruindo canal de controle");
                    _endpointResolver.Invalidate();
                    await DisposeHubAsync(stoppingToken);
                }
                else if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Reconnecting)
                {
                    // Durante reconexão automática (ex: após suspensão), manter proxy ativo
                    // para evitar que o aluno perca acesso enquanto o hub reconecta.
                    _logger.LogInformation("Hub em reconexão, mantendo proxy ativo");
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
                _logger.LogWarning("Servidor indisponível: {Message}", ex.Message);
                _endpointResolver.Invalidate();
                DisableProxyFailOpen();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no cliente");
                _endpointResolver.Invalidate();
                DisableProxyFailOpen();
            }

            // Exponential backoff: 2s, 4s, 8s, 16s, 32s, max 60s
            var delay = TimeSpan.FromMilliseconds(_retryDelayMs + Random.Shared.Next(0, 500));
            _logger.LogDebug("Aguardando {Delay}ms antes da próxima tentativa", _retryDelayMs);
            await Task.Delay(delay, stoppingToken);

            // Increase backoff for next attempt (capped at MaxRetryDelayMs)
            _retryDelayMs = Math.Min((int)(_retryDelayMs * 1.5), MaxRetryDelayMs);
        }

        if (!_failClosed)
        {
            WindowsProxyManager.SetProxy("", false);
        }
    }

    private void EnsureProxyEnabled(string proxyAddress)
    {
        if (_proxyAppliedThisRun &&
            _proxyEnabled &&
            string.Equals(_currentProxyAddress, proxyAddress, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        WindowsProxyManager.SetProxy(proxyAddress, true);
        _currentProxyAddress = proxyAddress;
        _proxyEnabled = true;
        _proxyAppliedThisRun = true;
        // Sincronizar com o coordenador global para o ProxyProtectionService
        ProxyStateCoordinator.SetProxyEnabled(proxyAddress);
        _logger.LogInformation("Proxy configurado: {ProxyAddress}", proxyAddress);
    }

    private void DisableProxyFailOpen()
    {
        if (_failClosed)
        {
            if (!string.IsNullOrWhiteSpace(_currentProxyAddress))
            {
                WindowsProxyManager.SetProxy(_currentProxyAddress, true);
                ProxyStateCoordinator.SetProxyEnabled(_currentProxyAddress);
            }
            _logger.LogWarning("Servidor indisponível: mantendo proxy ativo (modo fail-closed).");
            return;
        }
        if (!_proxyEnabled && string.IsNullOrEmpty(_currentProxyAddress))
        {
            return;
        }

        WindowsProxyManager.SetProxy("", false);
        _proxyEnabled = false;
        _proxyAppliedThisRun = true;
        _currentProxyAddress = null;
        // Sincronizar com o coordenador global para o ProxyProtectionService
        ProxyStateCoordinator.SetProxyDisabled("Servidor offline");
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
            await DisposeHubAsync(cancellationToken);
        }

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new JitterRetryPolicy())
            .WithServerTimeout(TimeSpan.FromSeconds(30))
            .WithKeepAliveInterval(TimeSpan.FromSeconds(10))
            .Build();

        _hubConnection.On("Disconnect", () =>
        {
            _logger.LogWarning("Servidor solicitou desconexao");
        });

        _hubConnection.Reconnecting += ex =>
        {
            _logger.LogWarning("Conexão com servidor em reconexão: {Message}", ex?.Message);
            // Não desabilitar o proxy durante reconexão automática
            // O proxy permanece ativo até que a reconexão falhe definitivamente
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += connectionId =>
        {
            Interlocked.Exchange(ref _needsRegistration, 1);
            _logger.LogInformation("Conexão com servidor restaurada ({ConnectionId}); registro será renovado", connectionId);
            return Task.CompletedTask;
        };

        _hubConnection.Closed += ex =>
        {
            Interlocked.Exchange(ref _needsRegistration, 1);
            _logger.LogWarning("Conexão com servidor encerrada: {Message}", ex?.Message);
            _endpointResolver.Invalidate();
            // Não desabilitar o proxy imediatamente - o loop principal
            // tentara reconectar no proximo ciclo e decidira se deve
            // manter ou remover o proxy baseado no estado da conexao
            return Task.CompletedTask;
        };

        _currentHubUrl = hubUrl;
    }

    private async Task RegisterWithServer(ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        var studentName = Environment.UserName;
        var hostname = Environment.MachineName;
        var os = Environment.OSVersion.VersionString;
        var mac = GetMacAddress();

        var payload = new
        {
            ip = GetLocalIp(),
            hostname,
            name = studentName,
            os,
            macAddress = mac,
            group = "default"
        };

        await _serverApi.PostAsync(endpoint, "/api/students/register", payload, cancellationToken);
        _logger.LogInformation("Registrado no servidor como {Name}", studentName);
    }

    private async Task EnsureProxyRootCertificateTrustedAsync(ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        var certificateEndpoint = $"{endpoint.Ip}:{endpoint.DashboardPort}";
        if (string.Equals(_lastCertificateEndpoint, certificateEndpoint, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_trustedRootThumbprint) &&
            DateTime.UtcNow - _lastCertificateCheckUtc < TimeSpan.FromMinutes(5))
        {
            return;
        }

        var certBytes = await _serverApi.GetCertificateAsync(endpoint, cancellationToken);
        using var rootCert = new X509Certificate2(certBytes);

        var thumbprint = rootCert.Thumbprint?.Replace(" ", "", StringComparison.Ordinal) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            throw new InvalidOperationException("Certificado raiz recebido sem thumbprint.");
        }

        ValidateRootCertificate(rootCert, thumbprint);

        if (string.Equals(_trustedRootThumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            _lastCertificateEndpoint = certificateEndpoint;
            _lastCertificateCheckUtc = DateTime.UtcNow;
            return;
        }

        if (CertificateExistsInRootStore(thumbprint, StoreLocation.LocalMachine) ||
            CertificateExistsInRootStore(thumbprint, StoreLocation.CurrentUser))
        {
            _trustedRootThumbprint = thumbprint;
            _lastCertificateEndpoint = certificateEndpoint;
            _lastCertificateCheckUtc = DateTime.UtcNow;
            return;
        }

        var installed = InstallRootCertificate(rootCert, StoreLocation.LocalMachine) ||
                        InstallRootCertificate(rootCert, StoreLocation.CurrentUser);

        if (!installed)
        {
            throw new InvalidOperationException("Não foi possível instalar certificado raiz do proxy.");
        }

        _trustedRootThumbprint = thumbprint;
        _lastCertificateEndpoint = certificateEndpoint;
        _lastCertificateCheckUtc = DateTime.UtcNow;
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

    private void ValidateRootCertificate(X509Certificate2 certificate, string thumbprint)
    {
        var now = DateTime.UtcNow;
        if (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now)
        {
            throw new InvalidOperationException("Certificado raiz recebido está expirado ou ainda não é válido.");
        }

        var basicConstraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
        if (basicConstraints is null || !basicConstraints.CertificateAuthority)
        {
            throw new InvalidOperationException("Certificado recebido não é uma autoridade certificadora raiz válida.");
        }

        if (!string.IsNullOrWhiteSpace(_pinnedRootThumbprint) &&
            !string.Equals(_pinnedRootThumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O certificado raiz recebido não corresponde ao thumbprint configurado.");
        }

        if (string.IsNullOrWhiteSpace(_pinnedRootThumbprint) && !_warnedUnpinnedCertificate)
        {
            _warnedUnpinnedCertificate = true;
            _logger.LogWarning("Server:RootCertificateThumbprint não foi configurado; a CA ainda é obtida por HTTP sem pinning.");
        }
    }

    private static string? NormalizeThumbprint(string? value)
    {
        var normalized = value?.Replace(" ", "", StringComparison.Ordinal).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
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
        if (!_failClosed)
        {
            WindowsProxyManager.SetProxy("", false);
        }
        if (_hubConnection is not null)
        {
            await DisposeHubAsync(cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task DisposeHubAsync(CancellationToken cancellationToken)
    {
        var connection = Interlocked.Exchange(ref _hubConnection, null);
        _currentHubUrl = null;
        if (connection is null) return;
        try { await connection.StopAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { await connection.DisposeAsync(); }
    }
}
