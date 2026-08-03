using Microsoft.AspNetCore.Mvc;
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

    [HttpGet]
    public IActionResult GetStatus()
    {
        return Ok(_updateService.Status);
    }

    [HttpPost("check")]
    public async Task<IActionResult> Check()
    {
        await _updateService.CheckForUpdatesAsync(HttpContext.RequestAborted);
        return Ok(_updateService.Status);
    }

    [HttpPost("download")]
    public async Task<IActionResult> Download()
    {
        await _updateService.DownloadUpdateAsync(HttpContext.RequestAborted);
        return Ok(_updateService.Status);
    }

    [HttpPost("install")]
    public async Task<IActionResult> Install()
    {
        await _updateService.InstallUpdateAsync(HttpContext.RequestAborted);
        return Accepted(_updateService.Status);
    }
}
