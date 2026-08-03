using System.Collections.Concurrent;
using System.Diagnostics;

namespace ProxyEdu.Server.Services;

/// <summary>
/// Métricas locais, sem dependência externa. Contadores são deliberadamente agregados:
/// URLs, headers e corpos não entram na telemetria.
/// </summary>
public sealed class RuntimeDiagnostics
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly OwnedTcpConnectionInspector _tcpInspector;
    private readonly ConcurrentDictionary<long, ProxyRequestTrace> _inFlight = new();
    private long _nextId;
    private long _requestsStarted, _requestsCompleted, _requestFailures, _slowRequests;
    private long _requestLatencyTicks, _tunnelSetups, _tunnelSetupTicks;
    private long _liteReadCount, _liteReadTicks, _liteWriteCount, _liteWriteTicks;
    private readonly ConcurrentDictionary<string, long> _exceptions = new(StringComparer.Ordinal);
    private TimeSpan _lastProcessorTime;
    private DateTime _lastCpuSampleUtc;
    private readonly object _sampleLock = new();

    public RuntimeDiagnostics(OwnedTcpConnectionInspector tcpInspector) => _tcpInspector = tcpInspector;

    public ProxyRequestTrace BeginRequest(string clientConnectionId, string serverConnectionId, bool isHttps, bool isConnect)
    {
        var trace = new ProxyRequestTrace(Interlocked.Increment(ref _nextId), clientConnectionId, serverConnectionId, isHttps, isConnect);
        _inFlight[trace.Id] = trace;
        Interlocked.Increment(ref _requestsStarted);
        return trace;
    }

    public RequestCompletion Complete(ProxyRequestTrace? trace, int statusCode)
    {
        if (trace is null || !_inFlight.TryRemove(trace.Id, out _)) return default;
        var elapsed = trace.Stopwatch.Elapsed;
        Interlocked.Increment(ref _requestsCompleted);
        Interlocked.Add(ref _requestLatencyTicks, elapsed.Ticks);
        if (elapsed > TimeSpan.FromSeconds(5)) Interlocked.Increment(ref _slowRequests);
        if (trace.IsConnect)
        {
            Interlocked.Increment(ref _tunnelSetups);
            Interlocked.Add(ref _tunnelSetupTicks, elapsed.Ticks);
        }
        return new RequestCompletion(elapsed, statusCode, trace.IsConnect, trace.IsHttps);
    }

    public void Fail(ProxyRequestTrace? trace)
    {
        if (trace is not null) _inFlight.TryRemove(trace.Id, out _);
        Interlocked.Increment(ref _requestFailures);
    }

    public void RecordException(Exception exception)
    {
        var kind = exception switch
        {
            System.Net.Sockets.SocketException => "SocketException",
            IOException => "IOException",
            System.Security.Authentication.AuthenticationException => "TlsAuthenticationException",
            _ => exception.GetType().Name
        };
        _exceptions.AddOrUpdate(kind, 1, static (_, current) => current + 1);
    }

    public void RecordLiteRead(TimeSpan elapsed) { Interlocked.Increment(ref _liteReadCount); Interlocked.Add(ref _liteReadTicks, elapsed.Ticks); }
    public void RecordLiteWrite(TimeSpan elapsed, int operations = 1) { Interlocked.Add(ref _liteWriteCount, operations); Interlocked.Add(ref _liteWriteTicks, elapsed.Ticks); }

    public object Snapshot(int proxyPort, int titaniumClientConnections, int signalRConnections, int connectedStudents, int logQueueSize, int studentBufferSize, int signalRQueueSize)
    {
        SweepExpiredRequests();
        _process.Refresh();
        var completed = Interlocked.Read(ref _requestsCompleted);
        var totalTicks = Interlocked.Read(ref _requestLatencyTicks);
        var tunnels = Interlocked.Read(ref _tunnelSetups);
        var tunnelTicks = Interlocked.Read(ref _tunnelSetupTicks);
        var gcInfo = GC.GetGCMemoryInfo();
        var tcp = _tcpInspector.GetSnapshot(_process.Id, proxyPort);

        return new
        {
            timestampUtc = DateTime.UtcNow,
            process = new
            {
                cpuPercentInstantaneous = SampleCpuPercent(),
                privateMemoryBytes = _process.PrivateMemorySize64,
                workingSetBytes = _process.WorkingSet64,
                threadCount = _process.Threads.Count,
                managedHeapBytes = GC.GetTotalMemory(false),
                heapSizeBytes = gcInfo.HeapSizeBytes,
                fragmentedBytes = gcInfo.FragmentedBytes,
                gcGen0 = GC.CollectionCount(0), gcGen1 = GC.CollectionCount(1), gcGen2 = GC.CollectionCount(2)
            },
            proxy = new
            {
                requestsStarted = Interlocked.Read(ref _requestsStarted),
                requestsCompleted = completed,
                requestsFailed = Interlocked.Read(ref _requestFailures),
                requestsInFlight = _inFlight.Count,
                slowRequestsOver5Seconds = Interlocked.Read(ref _slowRequests),
                averageRequestLatencyMs = completed == 0 ? 0 : TimeSpan.FromTicks(totalTicks / completed).TotalMilliseconds,
                observedTunnelSetups = tunnels,
                averageTunnelSetupMs = tunnels == 0 ? 0 : TimeSpan.FromTicks(tunnelTicks / tunnels).TotalMilliseconds,
                tcpEstablishedOnProxyPort = tcp.ProxyPortEstablished,
                titaniumClientConnections,
                signalRConnections,
                connectedStudents
            },
            tcpLifecycle = new
            {
                source = "TCP table filtered by ProxyEdu.Server PID",
                isAvailable = tcp.IsAvailable,
                proxyPort = new { established = tcp.ProxyPortEstablished, finWait2 = tcp.ProxyPortFinWait2, closeWait = tcp.ProxyPortCloseWait },
                serverProcess = new { established = tcp.ProcessEstablished, finWait2 = tcp.ProcessFinWait2, closeWait = tcp.ProcessCloseWait }
            },
            queues = new { logQueueSize, studentBufferSize, signalRActivityQueueSize = signalRQueueSize },
            liteDb = new
            {
                observedReads = Interlocked.Read(ref _liteReadCount),
                observedWrites = Interlocked.Read(ref _liteWriteCount),
                averageReadMs = AverageMilliseconds(_liteReadTicks, _liteReadCount),
                averageWriteMs = AverageMilliseconds(_liteWriteTicks, _liteWriteCount)
            },
            exceptions = _exceptions.ToDictionary(x => x.Key, x => x.Value)
        };
    }

    private static double AverageMilliseconds(long ticks, long count) => count == 0 ? 0 : TimeSpan.FromTicks(ticks / count).TotalMilliseconds;

    private void SweepExpiredRequests()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(2);
        foreach (var item in _inFlight.Where(x => x.Value.StartedUtc < cutoff))
        {
            if (_inFlight.TryRemove(item.Key, out _)) Interlocked.Increment(ref _requestFailures);
        }
    }

    private double SampleCpuPercent()
    {
        lock (_sampleLock)
        {
            var now = DateTime.UtcNow;
            var cpu = _process.TotalProcessorTime;
            if (_lastCpuSampleUtc == default)
            {
                _lastCpuSampleUtc = now; _lastProcessorTime = cpu; return 0;
            }
            var elapsed = (now - _lastCpuSampleUtc).TotalMilliseconds;
            var consumed = (cpu - _lastProcessorTime).TotalMilliseconds;
            _lastCpuSampleUtc = now; _lastProcessorTime = cpu;
            return elapsed <= 0 ? 0 : Math.Round(Math.Min(100, consumed / (elapsed * Environment.ProcessorCount) * 100), 1);
        }
    }

}

public sealed class ProxyRequestTrace
{
    public ProxyRequestTrace(long id, string clientConnectionId, string serverConnectionId, bool isHttps, bool isConnect)
    {
        Id = id; ClientConnectionId = clientConnectionId; ServerConnectionId = serverConnectionId;
        IsHttps = isHttps; IsConnect = isConnect; StartedUtc = DateTime.UtcNow;
    }
    public long Id { get; }
    public string ClientConnectionId { get; }
    public string ServerConnectionId { get; }
    public bool IsHttps { get; }
    public bool IsConnect { get; }
    public DateTime StartedUtc { get; }
    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
}

public readonly record struct RequestCompletion(TimeSpan Elapsed, int StatusCode, bool IsTunnelSetup, bool IsHttps);
