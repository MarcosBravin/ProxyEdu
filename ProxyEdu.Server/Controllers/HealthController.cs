using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Services;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly DatabaseService _db;

    public HealthController(DatabaseService db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var settings = _db.GetSettings();
        return Ok(new
        {
            status = "ok",
            serverName = Environment.MachineName,
            dashboardPort = settings.DashboardPort,
            proxyPort = settings.ProxyPort,
            enableHttpsInspection = settings.EnableHttpsInspection,
            timestamp = DateTime.UtcNow
        });
    }
}
