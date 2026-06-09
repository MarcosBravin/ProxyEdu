using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Services;
using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    private readonly DatabaseService _db;

    public LogsController(DatabaseService db) { _db = db; }

    [HttpGet]
    public IActionResult GetLogs(
        [FromQuery] string? studentId,
        [FromQuery] string? domain,
        [FromQuery] bool? blocked,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.Logs.FindAll().AsQueryable();
        if (!string.IsNullOrEmpty(studentId))
            query = query.Where(l => l.StudentId == studentId);
        if (!string.IsNullOrEmpty(domain))
            query = query.Where(l => l.Domain.Contains(domain));
        if (blocked.HasValue)
            query = query.Where(l => l.WasBlocked == blocked.Value);

        var total = query.Count();
        var items = query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpDelete]
    public IActionResult ClearLogs([FromQuery] string? studentId)
    {
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

    [HttpGet]
    public IActionResult Get() => Ok(_db.GetSettings());

    [HttpPut]
    public IActionResult Update([FromBody] ProxySettings settings)
    {
        if (settings.ProxyPort is < 1 or > 65535 || settings.DashboardPort is < 1 or > 65535)
            return BadRequest("Portas devem estar entre 1 e 65535.");
        if (settings.MaxLogRetentionDays is < 1 or > 3650)
            return BadRequest("Retencao de logs deve estar entre 1 e 3650 dias.");

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

    [HttpGet]
    public IActionResult GetAll() => Ok(_db.Groups.FindAll().ToList());

    [HttpPost]
    public IActionResult Create([FromBody] StudentGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.Name))
            return BadRequest("Nome do grupo e obrigatorio.");
        if (_db.Groups.Exists(g => g.Name == group.Name.Trim()))
            return BadRequest("Grupo ja existe.");

        group.Id = Guid.NewGuid().ToString();
        group.Name = group.Name.Trim();
        _db.Groups.Insert(group);
        return Ok(group);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        if (!_db.Groups.Delete(id)) return NotFound();
        return Ok(new { success = true });
    }
}
