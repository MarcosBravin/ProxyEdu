using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Security;
using ProxyEdu.Shared.Services;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/update")]
public sealed class UpdateController : ControllerBase
{
    private readonly UpdateService _updateService;

    public UpdateController(UpdateService updateService)
    {
        _updateService = updateService;
    }

    private bool IsAdmin() => HttpContext.IsAdmin();

    [HttpGet]
    public IActionResult GetStatus()
    {
        if (!IsAdmin()) return Forbid();
        return Ok(_updateService.Status);
    }

    [HttpPost("check")]
    public async Task<IActionResult> Check()
    {
        if (!IsAdmin()) return Forbid();
        await _updateService.CheckForUpdatesAsync(HttpContext.RequestAborted);
        return Ok(_updateService.Status);
    }

    [HttpPost("download")]
    public async Task<IActionResult> Download()
    {
        if (!IsAdmin()) return Forbid();
        await _updateService.DownloadUpdateAsync(HttpContext.RequestAborted);
        return Ok(_updateService.Status);
    }

    [HttpPost("install")]
    public async Task<IActionResult> Install()
    {
        if (!IsAdmin()) return Forbid();
        await _updateService.InstallUpdateAsync(HttpContext.RequestAborted);
        return Accepted(_updateService.Status);
    }
}
