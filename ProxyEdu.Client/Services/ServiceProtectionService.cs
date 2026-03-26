using System.Management;

namespace ProxyEdu.Client.Services;

/// <summary>
/// Serviço de proteção via WMI que monitora e protege o serviço do ProxyEdu Client.
/// Impede que usuários finalizem o serviço pelo Task Manager ou Gerenciador de Serviços.
/// </summary>
public class ServiceProtectionService : BackgroundService
{
    private readonly ILogger<ServiceProtectionService> _logger;
    private readonly string _serviceName;
    private ManagementEventWatcher? _stopWatcher;
    private ManagementEventWatcher? _startWatcher;
    private bool _isStopping;
    
    public ServiceProtectionService(
        ILogger<ServiceProtectionService> logger,
        IConfiguration config)
    {
        _logger = logger;
        _serviceName = config["Service:Name"] ?? "ProxyEduClient";
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Serviço de proteção WMI iniciado para: {ServiceName}", _serviceName);
        
        // Registrar watchers para eventos de início e parada
        RegisterServiceWatchers();
        
        // Manter o serviço vivo
        return Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private void RegisterServiceWatchers()
    {
        try
        {
            // WMI Query para monitorar tentativas de PARAR o serviço
            var stopQuery = new WqlEventQuery(
                "__InstanceModificationEvent",
                new TimeSpan(0, 0, 1),
                "TargetInstance ISA 'Win32_Service' AND " +
                $"TargetInstance.Name = '{_serviceName}' AND " +
                "TargetInstance.State = 'Stopped'");

            _stopWatcher = new ManagementEventWatcher(stopQuery);
            _stopWatcher.EventArrived += OnServiceStopAttempt;
            _stopWatcher.Start();
            
            _logger.LogInformation("Monitor WMI de parada de serviço iniciado");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao registrar monitor de parada de serviço");
        }

        try
        {
            // WMI Query para monitorar início do serviço
            var startQuery = new WqlEventQuery(
                "__InstanceModificationEvent",
                new TimeSpan(0, 0, 1),
                "TargetInstance ISA 'Win32_Service' AND " +
                $"TargetInstance.Name = '{_serviceName}' AND " +
                "TargetInstance.State = 'Running'");

            _startWatcher = new ManagementEventWatcher(startQuery);
            _startWatcher.EventArrived += OnServiceStart;
            _startWatcher.Start();
            
            _logger.LogInformation("Monitor WMI de início de serviço iniciado");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao registrar monitor de início de serviço");
        }
    }

    private void OnServiceStopAttempt(object sender, EventArrivedEventArgs e)
    {
        if (_isStopping) return;
        
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var serviceName = targetInstance["Name"]?.ToString();
            var state = targetInstance["State"]?.ToString();
            
            _logger.LogWarning("ALERTA: Tentativa de parada detectada para serviço: {ServiceName}", serviceName);
            
            // Registrar evento de segurança
            LogSecurityEvent("SERVICE_STOP_ATTEMPT", $"Tentativa de parar o serviço {serviceName}");
            
            // Tentar reiniciar o serviço imediatamente
            RestartService();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar tentativa de parada");
        }
    }

    private void OnServiceStart(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var serviceName = targetInstance["Name"]?.ToString();
            var state = targetInstance["State"]?.ToString();
            
            _logger.LogInformation("Serviço iniciado: {ServiceName}, Estado: {State}", serviceName, state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar início de serviço");
        }
    }

    private void RestartService()
    {
        try
        {
            _logger.LogWarning("Tentando reiniciar o serviço: {ServiceName}", _serviceName);
            
            // Usar WMI para reiniciar o serviço
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_Service WHERE Name = '{_serviceName}'");
            
            foreach (ManagementObject service in searcher.Get())
            {
                var result = service.InvokeMethod("StartService", null);
                var returnValue = Convert.ToInt32(result);
                
                if (returnValue == 0)
                {
                    _logger.LogWarning("Serviço reiniciado com sucesso: {ServiceName}", _serviceName);
                    LogSecurityEvent("SERVICE_RESTARTED", $"Serviço {_serviceName} foi reiniciado automaticamente");
                }
                else
                {
                    _logger.LogWarning("Falha ao reiniciar serviço. Código de retorno: {ReturnValue}", returnValue);
                    
                    // Tentar via sc.exe como fallback
                    RestartServiceViaSc();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao reiniciar serviço via WMI");
            
            // Tentar via sc.exe como fallback
            RestartServiceViaSc();
        }
    }

    private void RestartServiceViaSc()
    {
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"start {_serviceName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            process.WaitForExit(5000);
            
            if (process.ExitCode == 0)
            {
                _logger.LogWarning("Serviço reiniciado via sc.exe: {ServiceName}", _serviceName);
                LogSecurityEvent("SERVICE_RESTARTED_SC", $"Serviço {_serviceName} reiniciado via sc.exe");
            }
            else
            {
                _logger.LogError("Falha ao reiniciar via sc.exe. Código: {ExitCode}", process.ExitCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao reiniciar serviço via sc.exe");
        }
    }

    /// <summary>
    /// Bloqueia tentativas de parar o serviço definindo permissões de segurança.
    /// </summary>
    public void SetServiceProtection()
    {
        try
        {
            // Configurar o serviço para reiniciar automaticamente em caso de falha
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"failure \"{_serviceName}\" reset= 86400 actions= restart/5000/restart/5000/restart/15000",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            process.WaitForExit(5000);
            
            _logger.LogInformation("Configuração de recuperação do serviço aplicada");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao configurar proteção do serviço");
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
        _isStopping = true;
        
        _logger.LogInformation("Serviço de proteção WMI encerrando");
        
        try
        {
            _stopWatcher?.Stop();
            _stopWatcher?.Dispose();
            
            _startWatcher?.Stop();
            _startWatcher?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao parar watchers WMI");
        }
        
        await base.StopAsync(cancellationToken);
    }
}

