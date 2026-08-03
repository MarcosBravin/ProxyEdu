using Microsoft.Win32;
using ProxyEdu.Shared.Utils;
using System.Text;

namespace ProxyEdu.Client.Services;

/// <summary>
/// Serviço de proteção que monitora e força a configuração do proxy,
/// impedindo que alunos desativem o proxy local.
/// Agora usa WindowsProxyManager compartilhado ao invés de código duplicado.
/// </summary>
public class ProxyProtectionService : BackgroundService
{
    private readonly ILogger<ProxyProtectionService> _logger;
    private readonly ServerEndpointResolver _endpointResolver;
    private readonly ServerApiClient _serverApi;
    private readonly TimeSpan _checkInterval;
    private readonly bool _failClosed; // true = bloqueia acesso se não conseguir configurar proxy
    
    private string? _lastProxyServer;
    private bool _lastEnabled;
    private int _consecutiveFailures;
    private bool _serverAvailable;
    
    // Cache do último endpoint válido
    private ServerEndpoint? _lastValidEndpoint;
    private DateTime _lastEndpointCheck = DateTime.MinValue;
    
    public ProxyProtectionService(
        ILogger<ProxyProtectionService> logger,
        ServerEndpointResolver endpointResolver,
        ServerApiClient serverApi,
        IConfiguration config)
    {
        _logger = logger;
        _endpointResolver = endpointResolver;
        _serverApi = serverApi;
        
        // Intervalo de verificação rápido (2 segundos)
        var intervalSeconds = config.GetValue<int?>("Protection:CheckIntervalSeconds") ?? 2;
        if (intervalSeconds is < 1 or > 300)
        {
            throw new InvalidOperationException("Protection:CheckIntervalSeconds deve estar entre 1 e 300.");
        }
        _checkInterval = TimeSpan.FromSeconds(intervalSeconds);
        
        // Modo fail-closed: true = bloqueia tudo se não conseguir configurar proxy
        _failClosed = config.GetValue<bool?>("Protection:FailClosed") ?? false;
        
        // Initialize state from current Windows proxy settings
        var (currentProxy, enabled) = WindowsProxyManager.GetCurrentProxySettings();
        _lastProxyServer = currentProxy;
        _lastEnabled = enabled;
        
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
                
                // 2. Verificar estado do coordenador - respeitar fail-open intencional do Client
                var (clientEnabled, clientAddress, intentionalFailOpen) = ProxyStateCoordinator.GetCurrentState();
                
                if (intentionalFailOpen)
                {
                    // Client intencionalmente em fail-open (servidor offline).
                    // ProtectionService não deve forçar proxy.
                    _logger.LogDebug("Client em fail-open intencional, protection não vai forçar proxy");
                    
                    // Ainda detectar bypass para logging, mas sem forçar proxy
                    DetectBypassAttempts();
                }
                else
                {
                    // 3. Forçar configuração do proxy (apenas se Client quer proxy ativo)
                    await EnforceProxyAsync(stoppingToken);
                    
                    // 4. Detectar e registrar tentativas de bypass
                    DetectBypassAttempts();
                }
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
            
            try
            {
                _serverAvailable = await _serverApi.IsHealthyAsync(endpoint, cancellationToken);
            }
            catch
            {
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
            
            // Se servidor não disponível e modo fail-closed, manter proxy configurado
            // Se servidor disponível, forçar configuração correta
            bool shouldEnforce = true;
            
            if (!_serverAvailable && _failClosed)
            {
                // Em modo fail-closed, manter último proxy conhecido
                shouldEnforce = true;
                proxyAddress = _lastProxyServer ?? proxyAddress;
            }
            else if (!_serverAvailable && !_failClosed)
            {
                // Modo fail-open: só configurar se servidor disponível
                shouldEnforce = _serverAvailable;
            }

            if (shouldEnforce)
            {
                if (!_lastEnabled || !string.Equals(_lastProxyServer, proxyAddress, StringComparison.OrdinalIgnoreCase))
                {
                    WindowsProxyManager.SetProxy(proxyAddress, true);
                    _logger.LogInformation("Proxy reconciliado: {ProxyAddress}", proxyAddress);
                }
                _lastProxyServer = proxyAddress;
                _lastEnabled = true;
                _consecutiveFailures = 0;
                
                _logger.LogDebug("Proxy forçado: {ProxyAddress}", proxyAddress);
            }
        }
        catch (Exception)
        {
            _consecutiveFailures++;
            
            if (_consecutiveFailures >= 3)
            {
                _logger.LogWarning("Falhas consecutivas ao aplicar proxy: {Count}", _consecutiveFailures);
                
                // Em modo fail-closed, manter última configuração conhecida
                if (_failClosed && !string.IsNullOrEmpty(_lastProxyServer))
                {
                    WindowsProxyManager.SetProxy(_lastProxyServer, true);
                    _logger.LogWarning("Fail-closed: mantendo último proxy conhecido: {Proxy}", _lastProxyServer);
                }
            }
        }
    }

    private void DetectBypassAttempts()
    {
        try
        {
            var (currentProxy, proxyEnabled) = WindowsProxyManager.GetCurrentProxySettings();
            
            // Detectar se proxy foi desativado
            if (_lastEnabled && !proxyEnabled)
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
        catch
        {
            // Falha ao verificar não crítica
        }
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
            WindowsProxyManager.SetProxy(_lastProxyServer, true);
        }
        
        await base.StopAsync(cancellationToken);
    }
}
