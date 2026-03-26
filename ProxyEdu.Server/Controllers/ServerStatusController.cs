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

    public ServerStatusController(ServerHealthService healthService, StudentManagerService studentManager)
    {
        _healthService = healthService;
        _studentManager = studentManager;
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
            timestamp = stats.Timestamp
        });
    }
}

