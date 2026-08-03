using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Services;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly RuntimeDiagnostics _diagnostics;
    private readonly DatabaseService _db;
    private readonly StudentManagerService _students;
    private readonly LogQueueService _logs;
    private readonly StudentUpdateBuffer _buffer;
    private readonly ProxyServerService _proxy;
    private readonly ProxyHubConnectionRegistry _hubConnections;

    public DiagnosticsController(RuntimeDiagnostics diagnostics, DatabaseService db, StudentManagerService students, LogQueueService logs, StudentUpdateBuffer buffer, ProxyServerService proxy, ProxyHubConnectionRegistry hubConnections)
        => (_diagnostics, _db, _students, _logs, _buffer, _proxy, _hubConnections) = (diagnostics, db, students, logs, buffer, proxy, hubConnections);

    [HttpGet]
    public IActionResult Get()
    {
        var runtime = _proxy.GetRuntimeState();
        return Ok(_diagnostics.Snapshot(
        _db.GetSettings().ProxyPort,
        runtime.ClientConnections,
        _hubConnections.Count,
        _students.GetAll().Count(s => s.IsConnected),
        _logs.GetStats().QueueSize,
        _buffer.GetStats().BufferSize,
        _students.GetPendingActivityCount()));
    }
}
