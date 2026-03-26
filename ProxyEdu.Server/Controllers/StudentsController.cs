using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Services;
using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StudentsController : ControllerBase
{
    private readonly StudentManagerService _manager;
    private readonly DatabaseService _db;

    public StudentsController(StudentManagerService manager, DatabaseService db)
    {
        _manager = manager;
        _db = db;
    }

    [HttpGet]
    public IActionResult GetAll() => Ok(_manager.GetAll());

    [HttpGet("{id}")]
    public IActionResult Get(string id)
    {
        var s = _db.Students.FindById(id);
        if (s == null) return NotFound();
        return Ok(s);
    }

    [HttpPut("{id}")]
    public IActionResult Update(string id, [FromBody] StudentInfo updated)
    {
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
        _manager.SetStudentBlocked(id, true);
        return Ok(new { success = true });
    }

    [HttpPost("{id}/unblock")]
    public IActionResult Unblock(string id)
    {
        _manager.SetStudentBlocked(id, false);
        return Ok(new { success = true });
    }

    [HttpPost("{id}/release-all-sites")]
    public IActionResult ReleaseAllSites(string id)
    {
        _manager.SetStudentBypassFilters(id, true);
        return Ok(new { success = true });
    }

    [HttpPost("{id}/restore-filters")]
    public IActionResult RestoreFilters(string id)
    {
        _manager.SetStudentBypassFilters(id, false);
        return Ok(new { success = true });
    }

    [HttpPost("block-all")]
    public IActionResult BlockAll()
    {
        _manager.BlockAll();
        return Ok(new { success = true });
    }

    [HttpPost("unblock-all")]
    public IActionResult UnblockAll()
    {
        _manager.UnblockAll();
        return Ok(new { success = true });
    }

    [HttpPost("release-all-sites")]
    public IActionResult ReleaseAllSitesForAll()
    {
        _manager.ReleaseAllSitesForAll();
        return Ok(new { success = true });
    }

    [HttpPost("restore-filters")]
    public IActionResult RestoreFiltersForAll()
    {
        _manager.RestoreFiltersForAll();
        return Ok(new { success = true });
    }

    [HttpPost("group/{groupName}/block")]
    public IActionResult BlockGroup(string groupName)
    {
        _manager.SetGroupBlocked(groupName, true);
        return Ok(new { success = true });
    }

    [HttpPost("group/{groupName}/unblock")]
    public IActionResult UnblockGroup(string groupName)
    {
        _manager.SetGroupBlocked(groupName, false);
        return Ok(new { success = true });
    }

    [HttpGet("stats")]
    public IActionResult GetStats() => Ok(_manager.GetStats());

    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        _db.Students.Delete(id);
        return Ok(new { success = true });
    }
}
