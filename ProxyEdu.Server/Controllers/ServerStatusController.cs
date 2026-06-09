using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Services;
using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServerStatusController : ControllerBase
{
    private readonly ServerHealthService _healthService;
    private readonly StudentManagerService _studentManager;
    private readonly DatabaseService _db;
    private readonly ProxyServerService _proxyServerService;

    public ServerStatusController(
        ServerHealthService healthService,
        StudentManagerService studentManager,
        DatabaseService db,
        ProxyServerService proxyServerService)
    {
        _healthService = healthService;
        _studentManager = studentManager;
        _db = db;
        _proxyServerService = proxyServerService;
    }

    [HttpGet]
    public IActionResult GetStatus()
    {
        var healthStats = _healthService.GetHealthStats();
        
        // Get student connection info
        var students = _studentManager.GetAll();
        var connectedStudents = students.Count(s => s.IsConnected);

        // Add student info to response
        healthStats.ConnectedStudents = connectedStudents;
        healthStats.TotalStudents = students.Count;
        var settings = _db.GetSettings();
        healthStats.HttpsInspectionEnabled = settings.EnableHttpsInspection;
        healthStats.RootCertificateTrusted = _proxyServerService.IsRootCertificateTrusted();
        healthStats.HttpsProxyMode = settings.EnableHttpsInspection ? "Inspection" : "Tunnel";

        if (settings.EnableHttpsInspection && !healthStats.RootCertificateTrusted)
        {
            healthStats.Alerts.Add(new ServerAlert
            {
                Type = AlertType.Warning,
                Message = "Inspecao HTTPS ativada, mas o certificado raiz do proxy nao esta confiavel. O proxy mantera HTTPS em modo tunel para evitar erro de certificado.",
                Value = 0,
                Threshold = 1,
                Timestamp = DateTime.UtcNow
            });
        }

        return Ok(healthStats);
    }

    [HttpGet("alerts")]
    public IActionResult GetAlerts()
    {
        var stats = _healthService.GetHealthStats();
        return Ok(stats.Alerts);
    }

    [HttpGet("metrics")]
    public IActionResult GetMetrics()
    {
        var stats = _healthService.GetHealthStats();
        return Ok(new
        {
            cpu = stats.CpuUsagePercent,
            memory = stats.MemoryUsagePercent,
            memoryUsedMB = stats.MemoryUsageMB,
            memoryTotalMB = stats.MemoryTotalMB,
            connections = stats.ActiveConnections,
            networkSent = stats.NetworkBytesSent,
            networkReceived = stats.NetworkBytesReceived,
            uptime = stats.UptimeSeconds,
            httpsProxyMode = stats.HttpsProxyMode,
            httpsInspectionEnabled = stats.HttpsInspectionEnabled,
            rootCertificateTrusted = stats.RootCertificateTrusted,
            timestamp = stats.Timestamp
        });
    }
}

