using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Security;
using ProxyEdu.Server.Services;
using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    private readonly DatabaseService _db;

    public LogsController(DatabaseService db) { _db = db; }

    private bool IsAdmin() => HttpContext.IsAdmin();

    [HttpGet]
    public IActionResult GetLogs(
        [FromQuery] string? studentId,
        [FromQuery] string? domain,
        [FromQuery] bool? blocked,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (!IsAdmin()) return Forbid();
        // Efficient query using indexed LiteDB queries instead of loading all in memory
        var (items, total) = _db.QueryLogs(studentId, domain, blocked, page, pageSize);
        return Ok(new { total, page, pageSize, items });
    }

    [HttpDelete]
    public IActionResult ClearLogs([FromQuery] string? studentId)
    {
        if (!IsAdmin()) return Forbid();
        if (!string.IsNullOrEmpty(studentId))
            _db.Logs.DeleteMany(l => l.StudentId == studentId);
        else
            _db.Logs.DeleteAll();
        return Ok(new { success = true });
    }
}

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly DatabaseService _db;

    public SettingsController(DatabaseService db) { _db = db; }

    private bool IsAdmin() => HttpContext.IsAdmin();

    [HttpGet]
    public IActionResult Get()
    {
        if (!IsAdmin()) return Forbid();
        return Ok(_db.GetSettings());
    }

    [HttpPut]
    public IActionResult Update([FromBody] ProxySettings settings)
    {
        if (!IsAdmin()) return Forbid();
        var validationError = ProxySettingsValidator.Validate(settings);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var current = _db.GetSettings();
        if (settings.ProxyPort != current.ProxyPort || settings.DashboardPort != current.DashboardPort)
        {
            return Conflict(new
            {
                error = "As portas são definidas no início do serviço e não podem ser alteradas pelo painel em execução.",
                currentProxyPort = current.ProxyPort,
                currentDashboardPort = current.DashboardPort
            });
        }
        _db.SaveSettings(settings);
        return Ok(settings);
    }
}

[ApiController]
[Route("api/[controller]")]
public class GroupsController : ControllerBase
{
    private readonly DatabaseService _db;

    public GroupsController(DatabaseService db) { _db = db; }

    private bool IsAdmin() => HttpContext.IsAdmin();

    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(_db.Groups.FindAll().ToList());
    }

    [HttpPost]
    public IActionResult Create([FromBody] StudentGroup group)
    {
        if (!IsAdmin()) return Forbid();
        group.Id = Guid.NewGuid().ToString();
        _db.Groups.Insert(group);
        return Ok(group);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        if (!IsAdmin()) return Forbid();
        _db.Groups.Delete(id);
        return Ok(new { success = true });
    }
}
