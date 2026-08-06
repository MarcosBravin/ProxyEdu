using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Services;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private static readonly DateTime StartTime = DateTime.UtcNow;
    private static readonly string ApplicationVersion =
        typeof(HealthController).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    private readonly ProxyServerService _proxy;
    private readonly DatabaseService _database;

    public HealthController(ProxyServerService proxy, DatabaseService database)
    {
        _proxy = proxy;
        _database = database;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            uptime = (DateTime.UtcNow - StartTime).TotalSeconds,
            version = ApplicationVersion,
            server = Environment.MachineName
        });
    }

    [HttpGet("ready")]
    public IActionResult Ready()
    {
        try
        {
            var runtime = _proxy.GetRuntimeState();
            var settings = _database.GetSettings();
            var ready = runtime.IsRunning && _proxy.GetRootCertificate() is not null && settings.ProxyPort is > 0 and <= 65535;
            return ready
                ? Ok(new { status = "ready", proxyPort = settings.ProxyPort, timestamp = DateTime.UtcNow })
                : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "not_ready", proxyRunning = runtime.IsRunning, timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "not_ready", reason = ex.GetType().Name, timestamp = DateTime.UtcNow });
        }
    }
}

