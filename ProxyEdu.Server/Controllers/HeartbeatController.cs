using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Services;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/students")]
public class HeartbeatController : ControllerBase
{
    private readonly StudentManagerService _manager;

    public HeartbeatController(StudentManagerService manager)
    {
        _manager = manager;
    }

    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest req)
    {
        var student = _manager.RegisterOrUpdate(
            req.Ip ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            req.Hostname ?? "",
            req.Name ?? "",
            req.Os ?? "",
            req.MacAddress ?? "",
            req.Group ?? "default"
        );
        return Ok(student);
    }

    [HttpPost("heartbeat")]
    public IActionResult Heartbeat([FromBody] HeartbeatRequest req)
    {
        var ip = req.Ip ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        _manager.TouchHeartbeat(ip, req.CurrentUrl);
        return Ok();
    }
}

public class RegisterRequest
{
    public string? Ip { get; set; }
    public string? Hostname { get; set; }
    public string? Name { get; set; }
    public string? Os { get; set; }
    public string? MacAddress { get; set; }
    public string? Group { get; set; }
}

public class HeartbeatRequest
{
    public string? Ip { get; set; }
    public string? CurrentUrl { get; set; }
}
