using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;

namespace ProxyEdu.Client.Services;

/// <summary>
/// Serviço de proteção que monitora e força a configuração do proxy,
/// impedindo que alunos desativem o proxy local.
/// </summary>
public class ProxyProtectionService : BackgroundService
{
    private readonly ILogger<ProxyProtectionService> _logger;
    private readonly ServerEndpointResolver _endpointResolver;
    private readonly string _protectedProxyAddress;
    private readonly TimeSpan _checkInterval;
    private readonly bool _failClosed; // true = bloqueia acesso se não conseguir configurar proxy
    
    private string? _lastProxyServer;
    private int _lastProxyEnable;
    private int _consecutiveFailures;
    private bool _serverAvailable;
    
    // Cache do último endpoint válido
    private ServerEndpoint? _lastValidEndpoint;
    private DateTime _lastEndpointCheck = DateTime.MinValue;
    
    public ProxyProtectionService(
        ILogger<ProxyProtectionService> logger,
        ServerEndpointResolver endpointResolver,
        IConfiguration config)
    {
        _logger = logger;
        _endpointResolver = endpointResolver;
        
        // Carregar configurações
        var proxyPort = config["Server:ProxyPort"] ?? "8888";
        var dashboardPort = config["Server:DashboardPort"] ?? "5000";
        _protectedProxyAddress = $"127.0.0.1:{proxyPort}";
        
        // Intervalo de verificação rápido (2 segundos)
        var intervalSeconds = config.GetValue<int?>("Protection:CheckIntervalSeconds") ?? 2;
        _checkInterval = TimeSpan.FromSeconds(intervalSeconds);
        
        // Modo fail-closed: true = bloqueia tudo se não conseguir configurar proxy
        _failClosed = config.GetValue<bool?>("Protection:FailClosed") ?? false;
        
        _logger.LogInformation("Serviço de proteção iniciado. Fail-closed: {FailClosed}", _failClosed);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Monitor de proteção do proxy iniciado");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Verificar disponibilidade do servidor
                await CheckServerAvailabilityAsync(stoppingToken);
                
                // 2. Forçar configuração do proxy
                await EnforceProxyAsync(stoppingToken);
                
                // 3. Detectar e registrar tentativas de bypass
                DetectBypassAttempts();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no serviço de proteção");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CheckServerAvailabilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Cache endpoint por 30 segundos
            if (_lastValidEndpoint is not null && 
                (DateTime.UtcNow - _lastEndpointCheck).TotalSeconds < 30)
            {
                return;
            }

            var endpoint = await _endpointResolver.ResolveAsync(cancellationToken);
            
            // Testar connectivity com servidor
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            try
            {
                var response = await client.GetAsync(
                    $"http://{endpoint.Ip}:{endpoint.DashboardPort}/api/health", 
                    cancellationToken);
                _serverAvailable = response.IsSuccessStatusCode;
            }
            catch
            {
                // Tentar endpoint alternativo
                _serverAvailable = false;
            }

            _lastValidEndpoint = endpoint;
            _lastEndpointCheck = DateTime.UtcNow;
            
            if (_serverAvailable)
            {
                _consecutiveFailures = 0;
            }
        }
        catch (Exception ex)
        {
            _serverAvailable = false;
            _logger.LogDebug("Servidor indisponível: {Message}", ex.Message);
        }
    }

    private async Task EnforceProxyAsync(CancellationToken stoppingToken)
    {
        try
        {
            var endpoint = await _endpointResolver.ResolveAsync(stoppingToken);
            var proxyAddress = $"{endpoint.Ip}:{endpoint.ProxyPort}";
            
            // Verificar configuração atual do proxy
            var (currentProxy, proxyEnabled) = GetCurrentProxySettings();
            
            // Se servidor não disponível e modo fail-closed, manter proxy configurado
            // Se servidor disponível, forçar configuração correta
            bool shouldEnforce = true;
            
            if (!_serverAvailable && _failClosed)
            {
                // Em modo fail-closed, manter último proxy conhecido
                shouldEnforce = !string.IsNullOrEmpty(_lastProxyServer);
                proxyAddress = _lastProxyServer ?? proxyAddress;
            }
            else if (!_serverAvailable && !_failClosed)
            {
                // Modo fail-open: só configurar se servidor disponível
                shouldEnforce = _serverAvailable;
            }

            if (shouldEnforce)
            {
                SetWindowsProxy(proxyAddress, true);
                _lastProxyServer = proxyAddress;
                _lastProxyEnable = 1;
                _consecutiveFailures = 0;
                
                _logger.LogDebug("Proxy forçado: {ProxyAddress}", proxyAddress);
            }
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            
            if (_consecutiveFailures >= 3)
            {
                _logger.LogWarning("Falha consecutivas ao aplicar proxy: {Count}", _consecutiveFailures);
                
                // Em modo fail-closed, manter última configuração conhecida
                if (_failClosed && !string.IsNullOrEmpty(_lastProxyServer))
                {
                    SetWindowsProxy(_lastProxyServer, true);
                    _logger.LogWarning("Fail-closed: mantendo último proxy known: {Proxy}", _lastProxyServer);
                }
            }
        }
    }

    private void DetectBypassAttempts()
    {
        try
        {
            var (currentProxy, proxyEnabled) = GetCurrentProxySettings();
            
            // Detectar se proxy foi desativado
            if (_lastProxyEnable == 1 && proxyEnabled == 0)
            {
                _logger.LogWarning("ALERTA: Tentativa de desativação do proxy detectada!");
                
                // Log de segurança
                LogSecurityEvent("PROXY_DISABLED", "Proxy foi desativado por usuário não autorizado");
            }
            
            // Detectar mudança de servidor proxy
            if (_lastProxyServer is not null && 
                !string.Equals(currentProxy, _lastProxyServer, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(currentProxy))
            {
                _logger.LogWarning("ALERTA: Mudança de servidor proxy detectada! De {Old} para {New}", 
                    _lastProxyServer, currentProxy);
                
                LogSecurityEvent("PROXY_CHANGED", $"Proxy alterado de {_lastProxyServer} para {currentProxy}");
            }
            
            // Detectar uso de PAC (Proxy Auto-Config)
            DetectPacFileUsage();
            
            // Detectar bypass via WinHTTP
            DetectWinHttpBypass();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao detectar tentativas de bypass");
        }
    }

    private void DetectPacFileUsage()
    {
        try
        {
            using var regKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            
            if (regKey is null) return;
            
            var pacUrl = regKey.GetValue("AutoConfigURL") as string;
            if (!string.IsNullOrEmpty(pacUrl))
            {
                _logger.LogWarning("ALERTA: Configuração PAC detectada: {PacUrl}", pacUrl);
                LogSecurityEvent("PAC_DETECTED", $"PAC configurado: {pacUrl}");
                
                // Remover configuração PAC
                try
                {
                    using var writeKey = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                    writeKey?.DeleteValue("AutoConfigURL", false);
                    _logger.LogInformation("Configuração PAC removida");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha ao remover configuração PAC");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Erro ao verificar PAC: {Message}", ex.Message);
        }
    }

    private void DetectWinHttpBypass()
    {
        try
        {
            // Verificar configurações WinHTTP que podem fazer bypass
            using var regKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings");
            
            if (regKey is null) return;
            
            // Verificar ProxyOverride (list of addresses that bypass proxy)
            var overrideList = regKey.GetValue("ProxyOverride") as string;
            if (overrideList is not null && overrideList.Contains("*"))
            {
                _logger.LogWarning("ALERTA: Bypass wildcard (*) detectado nas configurações");
                LogSecurityEvent("WILDCARD_BYPASS", "Bypass com wildcard detectado");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Erro ao verificar WinHTTP: {Message}", ex.Message);
        }
    }

    private (string? proxy, int enabled) GetCurrentProxySettings()
    {
        try
        {
            using var regKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            
            if (regKey is null)
            {
                return (null, 0);
            }

            var proxyServer = regKey.GetValue("ProxyServer") as string;
            var proxyEnable = regKey.GetValue("ProxyEnable") as int? ?? 0;
            
            return (proxyServer, proxyEnable);
        }
        catch
        {
            return (null, 0);
        }
    }

    private void SetWindowsProxy(string proxyAddress, bool enable)
    {
        // Aplicar ao usuário atual
        ApplyProxyToCurrentUser(proxyAddress, enable);
        
        // Aplicar a todos os usuários logados
        ApplyProxyToAllLoadedUsers(proxyAddress, enable);
        
        // Forçar refresh das configurações
        RefreshInternetSettings();
        
        // Também configurar WinHTTP
        ConfigureWinHttp(proxyAddress, enable);
    }

    private static void ApplyProxyToCurrentUser(string proxyAddress, bool enable)
    {
        try
        {
            using var regKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
            
            if (regKey is null) return;

            if (enable)
            {
                regKey.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                regKey.SetValue("ProxyServer", proxyAddress, RegistryValueKind.String);
                // Lista de bypass segura - apenas localhost
                regKey.SetValue("ProxyOverride", "localhost;127.*;<local>", RegistryValueKind.String);
                
                // Remover configurações de PAC
                regKey.DeleteValue("AutoConfigURL", false);
                regKey.DeleteValue("AutoConfigDetect", false);
            }
            else
            {
                regKey.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao aplicar proxy ao usuário atual: {ex.Message}");
        }
    }

    private static void ApplyProxyToAllLoadedUsers(string proxyAddress, bool enable)
    {
        try
        {
            using var usersRoot = Registry.Users;
            foreach (var sid in usersRoot.GetSubKeyNames())
            {
                if (!IsUserSid(sid)) continue;

                try
                {
                    using var regKey = usersRoot.OpenSubKey(
                        $@"{sid}\Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                    
                    if (regKey is null) continue;

                    if (enable)
                    {
                        regKey.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                        regKey.SetValue("ProxyServer", proxyAddress, RegistryValueKind.String);
                        regKey.SetValue("ProxyOverride", "localhost;127.*;<local>", RegistryValueKind.String);
                        regKey.DeleteValue("AutoConfigURL", false);
                    }
                    else
                    {
                        regKey.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    }
                }
                catch
                {
                    // Permissão negada para alguns SIDs é normal
                }
            }
        }
        catch
        {
            // Erro ao acessar usuários
        }
    }

    private void ConfigureWinHttp(string proxyAddress, bool enable)
    {
        try
        {
            // Configurar proxy WinHTTP via netsh
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = enable 
                        ? $"winhttp set proxy proxy-server=\"{proxyAddress}\" bypass-list=\"localhost;127.*\""
                        : "winhttp reset proxy",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            process.WaitForExit(5000);
            
            _logger.LogDebug("WinHTTP proxy configurado: {Enable}", enable);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao configurar WinHTTP");
        }
    }

    [System.Runtime.InteropServices.DllImport("wininet.dll")]
    private static extern bool InternetSetOption(
        IntPtr hInternet, 
        int dwOption, 
        IntPtr lpBuffer, 
        int dwBufferLength);

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    private static void RefreshInternetSettings()
    {
        try
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch
        {
            // Ignorar erros de refresh
        }
    }

    private static bool IsUserSid(string sid)
    {
        return sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) ||
               sid.StartsWith("S-1-12-1-", StringComparison.OrdinalIgnoreCase);
    }

    private void LogSecurityEvent(string eventType, string details)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ProxyEdu",
                "security.log");
            
            var dir = Path.GetDirectoryName(logPath);
            if (dir is not null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            var logEntry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{eventType}] {details}{Environment.NewLine}";
            File.AppendAllText(logPath, logEntry);
            
            _logger.LogInformation("Evento de segurança registrado: {EventType}", eventType);
        }
        catch
        {
            // Falha ao registrar log não deve interromper o serviço
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Serviço de proteção encerrando");
        
        // Em modo fail-closed, não desativar proxy ao parar
        if (_failClosed && !string.IsNullOrEmpty(_lastProxyServer))
        {
            _logger.LogInformation("Fail-closed: mantendo proxy ativo ao encerrar serviço");
            SetWindowsProxy(_lastProxyServer, true);
        }
        
        await base.StopAsync(cancellationToken);
    }
}

