using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Security;
using ProxyEdu.Server.Services;
using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StudentsController : ControllerBase
{
    private const int MaximumTemporaryAccessMinutes = 24 * 60;
    private readonly StudentManagerService _manager;
    private readonly DatabaseService _db;

    public StudentsController(StudentManagerService manager, DatabaseService db)
    {
        _manager = manager;
        _db = db;
    }

    private bool IsAdmin() => HttpContext.IsAdmin();
    private bool IsProfessor() => HttpContext.IsProfessor();
    private bool CanViewStudentData() => IsAdmin() || IsProfessor();

    [HttpGet]
    public IActionResult GetAll()
    {
        if (!CanViewStudentData()) return Forbid();
        return Ok(_manager.GetAll());
    }

    [HttpGet("{id}")]
    public IActionResult Get(string id)
    {
        if (!CanViewStudentData()) return Forbid();
        var s = _db.Students.FindById(id);
        if (s == null) return NotFound();
        return Ok(s);
    }

    [HttpPut("{id}")]
    public IActionResult Update(string id, [FromBody] StudentInfo updated)
    {
        if (!IsAdmin()) return Forbid();
        var student = _db.Students.FindById(id);
        if (student == null) return NotFound();
        student.Name = updated.Name;
        student.Group = updated.Group;
        _db.Students.Update(student);
        return Ok(student);
    }

    [HttpPost("{id}/block")]
    public IActionResult Block(string id)
    {
        if (!IsAdmin()) return Forbid();
        _manager.SetStudentBlocked(id, true);
        return Ok(new { success = true });
    }

    [HttpPost("{id}/unblock")]
    public IActionResult Unblock(string id)
    {
        if (!IsAdmin()) return Forbid();
        _manager.SetStudentBlocked(id, false);
        return Ok(new { success = true });
    }

    [HttpPost("{id}/temporary-access")]
    public IActionResult GrantTemporaryAccess(string id, [FromBody] TemporaryAccessRequest request)
    {
        if (!CanViewStudentData()) return Forbid();
        if (request == null || request.Minutes <= 0 || request.Minutes > MaximumTemporaryAccessMinutes)
        {
            return BadRequest(new { error = $"A duração deve estar entre 1 e {MaximumTemporaryAccessMinutes} minutos." });
        }

        var student = _manager.SetStudentTemporaryAccess(id, TimeSpan.FromMinutes(request.Minutes));
        if (student == null) return NotFound(new { error = "Aluno não encontrado." });

        return Ok(new
        {
            success = true,
            expiresAtUtc = student.TemporaryAccessUntilUtc,
            student
        });
    }

    [HttpDelete("{id}/temporary-access")]
    public IActionResult CancelTemporaryAccess(string id)
    {
        if (!CanViewStudentData()) return Forbid();

        var student = _manager.CancelStudentTemporaryAccess(id);
        if (student == null) return NotFound(new { error = "Aluno não encontrado." });

        return Ok(new { success = true, student });
    }

    [HttpPost("{id}/release-all-sites")]
    public IActionResult ReleaseAllSites(string id)
    {
        if (!IsAdmin()) return Forbid();
        _manager.SetStudentBypassFilters(id, true);
        return Ok(new { success = true });
    }

    [HttpPost("{id}/restore-filters")]
    public IActionResult RestoreFilters(string id)
    {
        if (!IsAdmin()) return Forbid();
        _manager.SetStudentBypassFilters(id, false);
        return Ok(new { success = true });
    }

    [HttpPost("block-all")]
    public IActionResult BlockAll()
    {
        if (!IsAdmin()) return Forbid();
        _manager.BlockAll();
        return Ok(new { success = true });
    }

    [HttpPost("unblock-all")]
    public IActionResult UnblockAll()
    {
        if (!IsAdmin()) return Forbid();
        _manager.UnblockAll();
        return Ok(new { success = true });
    }

    [HttpPost("release-all-sites")]
    public IActionResult ReleaseAllSitesForAll()
    {
        if (!IsAdmin()) return Forbid();
        _manager.ReleaseAllSitesForAll();
        return Ok(new { success = true });
    }

    [HttpPost("restore-filters")]
    public IActionResult RestoreFiltersForAll()
    {
        if (!IsAdmin()) return Forbid();
        _manager.RestoreFiltersForAll();
        return Ok(new { success = true });
    }

    [HttpPost("group/{groupName}/block")]
    public IActionResult BlockGroup(string groupName)
    {
        if (!IsAdmin()) return Forbid();
        _manager.SetGroupBlocked(groupName, true);
        return Ok(new { success = true });
    }

    [HttpPost("group/{groupName}/unblock")]
    public IActionResult UnblockGroup(string groupName)
    {
        if (!IsAdmin()) return Forbid();
        _manager.SetGroupBlocked(groupName, false);
        return Ok(new { success = true });
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        if (!IsAdmin()) return Forbid();
        return Ok(_manager.GetStats());
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        if (!IsAdmin()) return Forbid();
        _db.Students.Delete(id);
        return Ok(new { success = true });
    }
}

public class TemporaryAccessRequest
{
    public int Minutes { get; set; }
}
