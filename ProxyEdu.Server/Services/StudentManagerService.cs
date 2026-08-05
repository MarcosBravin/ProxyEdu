using System.Collections.Concurrent;
using ProxyEdu.Shared.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using ProxyEdu.Server.Hubs;

namespace ProxyEdu.Server.Services;

public class StudentManagerService : IHostedService
{
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StatsCacheDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(2);
    private const string StatsCacheKey = "DashboardStats";

    private readonly DatabaseService _db;
    private readonly IHubContext<ProxyHub> _hub;
    private readonly IMemoryCache _cache;
    private readonly ILogger<StudentManagerService> _logger;
    private readonly StudentUpdateBuffer _studentBuffer;

    // Lock-free presence tracking using ConcurrentDictionary
    private readonly ConcurrentDictionary<string, DateTime> _presenceByIp = new(StringComparer.OrdinalIgnoreCase);

    // Activity buffer para broadcasts agregados (reduz SignalR de milhares para ~5/segundo)
    private readonly ConcurrentQueue<object> _activityBuffer = new();
    private long _totalActivitiesBuffered;
    private long _droppedActivities;
    private int _pendingActivities;
    private const int MaxPendingActivities = 10_000;
    private CancellationTokenSource? _broadcastCts;
    private Task? _broadcastTask;

    public StudentManagerService(
        DatabaseService db,
        IHubContext<ProxyHub> hub,
        IMemoryCache cache,
        ILogger<StudentManagerService> logger,
        StudentUpdateBuffer studentBuffer)
    {
        _db = db;
        _hub = hub;
        _cache = cache;
        _logger = logger;
        _studentBuffer = studentBuffer;

    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _broadcastCts = new CancellationTokenSource();
        _broadcastTask = BroadcastAggregatedActivityLoopAsync(_broadcastCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_broadcastCts is null || _broadcastTask is null) return;
        _broadcastCts.Cancel();
        try { await _broadcastTask.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (_broadcastCts.IsCancellationRequested) { }
        finally { _broadcastCts.Dispose(); _broadcastCts = null; _broadcastTask = null; }
    }

    /// <summary>
    /// Loop que envia atividades agregadas a cada 2 segundos.
    /// Em vez de enviar SignalR por request, bufferiza e envia em lote.
    /// </summary>
    private async Task BroadcastAggregatedActivityLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(BroadcastInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (Volatile.Read(ref _pendingActivities) == 0)
                    continue;

                // Coleta lote atual (máximo 100 atividades por broadcast)
                var batch = new List<object>();
                while (batch.Count < 100 && _activityBuffer.TryDequeue(out var activity))
                {
                    Interlocked.Decrement(ref _pendingActivities);
                    batch.Add(activity);
                }

                if (batch.Count > 0)
                {
                    await _hub.Clients.All.SendAsync("StudentActivityBatch", batch, stoppingToken);
                    Interlocked.Add(ref _totalActivitiesBuffered, batch.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro no broadcast agregado de atividades");
            }
        }
    }

    public int GetPendingActivityCount() => Volatile.Read(ref _pendingActivities);

    private void QueueActivity(object activity)
    {
        if (Interlocked.Increment(ref _pendingActivities) > MaxPendingActivities)
        {
            Interlocked.Decrement(ref _pendingActivities);
            Interlocked.Increment(ref _droppedActivities);
            return;
        }
        _activityBuffer.Enqueue(activity);
    }

    private void Publish(string eventName, object payload)
    {
        _ = PublishAsync(eventName, payload);
    }

    private async Task PublishAsync(string eventName, object payload)
    {
        try { await _hub.Clients.All.SendAsync(eventName, payload); }
        catch (Exception ex) { _logger.LogWarning(ex, "SignalR publish failed for {EventName}", eventName); }
    }

    public StudentInfo RegisterOrUpdate(string ip, string hostname, string name, string os, string macAddress, string group)
    {
        var normalizedIp = IpAddressNormalizer.Normalize(ip);
        if (string.IsNullOrWhiteSpace(normalizedIp))
        {
            throw new ArgumentException("IP invalido para registro.", nameof(ip));
        }

        TouchPresence(normalizedIp);

        var now = DateTime.UtcNow;
        var existing = FindStudentByIp(normalizedIp);
        if (existing == null)
        {
            existing = new StudentInfo
            {
                IpAddress = normalizedIp,
                Hostname = hostname,
                Name = string.IsNullOrEmpty(name) ? hostname : name,
                Os = os,
                MacAddress = macAddress,
                Group = string.IsNullOrWhiteSpace(group) ? "default" : group,
                IsConnected = true,
                ConnectedAt = now,
                LastSeen = now
            };
            _db.Students.Insert(existing);
        }
        else
        {
            existing.IpAddress = normalizedIp;
            existing.IsConnected = true;
            existing.LastSeen = now;
            if (!string.IsNullOrEmpty(name)) existing.Name = name;
            if (!string.IsNullOrEmpty(hostname)) existing.Hostname = hostname;
            if (!string.IsNullOrEmpty(macAddress)) existing.MacAddress = macAddress;
            if (!string.IsNullOrWhiteSpace(group)) existing.Group = group;
            if (!string.IsNullOrWhiteSpace(os)) existing.Os = os;
            _db.Students.Update(existing);
        }

        InvalidateStatsCache();
        var snapshot = MaterializeForDashboard(existing);
        Publish("StudentUpdated", snapshot);
        return snapshot;
    }

    public void TouchHeartbeat(string ip, string? currentUrl = null)
    {
        var normalizedIp = IpAddressNormalizer.Normalize(ip);
        if (string.IsNullOrWhiteSpace(normalizedIp)) return;

        TouchPresence(normalizedIp);

        // Não faz broadcast de heartbeat individual - o loop agregado cuida disso
    }

    public void UpdateActivity(string ip, string url, bool blocked, long bytes)
    {
        var normalizedIp = IpAddressNormalizer.Normalize(ip);
        if (string.IsNullOrWhiteSpace(normalizedIp)) return;

        TouchPresence(normalizedIp);

        var student = FindStudentByIp(normalizedIp);
        if (student == null) return;

        // Atualiza em memória (sem escrever no LiteDB a cada request)
        student.LastSeen = DateTime.UtcNow;
        student.CurrentUrl = url;
        student.TotalRequests++;
        student.BytesTransferred += bytes;
        if (blocked) student.BlockedRequests++;

        // Bufferiza a atualização no LiteDB (escrita em lote a cada 5s)
        _studentBuffer.BufferUpdate(student);

        // Bufferiza atividade para broadcast agregado (evita SignalR por request)
        QueueActivity(new
        {
            studentId = student.Id,
            ip = normalizedIp,
            url,
            blocked,
            timestamp = DateTime.UtcNow
        });
    }

    public void SetStudentBlocked(string studentId, bool blocked)
    {
        var student = _db.Students.FindById(studentId);
        if (student == null) return;
        student.IsBlocked = blocked;
        if (blocked) student.BypassFilters = false;
        _db.Students.Update(student);
        InvalidateStatsCache();

        Publish("StudentUpdated", MaterializeForDashboard(student));
    }

    public void SetGroupBlocked(string groupName, bool blocked)
    {
        var students = _db.Students.Find(s => s.Group == groupName).ToList();
        foreach (var s in students)
        {
            s.IsBlocked = blocked;
            if (blocked) s.BypassFilters = false;
            _db.Students.Update(s);
        }

        InvalidateStatsCache();
        Publish("GroupUpdated", new { group = groupName, blocked });
    }

    public void BlockAll() => SetAllBlocked(true);
    public void UnblockAll() => SetAllBlocked(false);
    public void ReleaseAllSitesForAll() => SetAllBypassFilters(true);
    public void RestoreFiltersForAll() => SetAllBypassFilters(false);

    private void SetAllBlocked(bool blocked)
    {
        var all = _db.Students.FindAll().ToList();
        foreach (var s in all)
        {
            s.IsBlocked = blocked;
            if (blocked) s.BypassFilters = false;
            _db.Students.Update(s);
        }
        InvalidateStatsCache();
        Publish("AllStudentsUpdated", new { blocked });
    }

    public void SetStudentBypassFilters(string studentId, bool bypass)
    {
        var student = _db.Students.FindById(studentId);
        if (student == null) return;
        student.BypassFilters = bypass;
        if (bypass) student.IsBlocked = false;
        _db.Students.Update(student);
        InvalidateStatsCache();
        Publish("StudentUpdated", MaterializeForDashboard(student));
    }

    public void SetStudentTemporaryAccess(string studentId, TimeSpan duration)
    {
        var student = _db.Students.FindById(studentId);
        if (student == null) return;

        student.TemporaryAccessPreviousBlockedState = student.IsBlocked;
        student.IsBlocked = false;
        student.BypassFilters = true;
        student.TemporaryAccessUntilUtc = DateTime.UtcNow.Add(duration);

        _db.Students.Update(student);
        InvalidateStatsCache();
        Publish("StudentUpdated", MaterializeForDashboard(student));
    }

    private StudentInfo? GetStudentByIpAndExpireTemporaryAccess(string ip)
    {
        var student = FindStudentByIp(ip);
        if (student == null) return null;

        if (student.TemporaryAccessUntilUtc.HasValue && student.TemporaryAccessUntilUtc.Value <= DateTime.UtcNow)
        {
            student.TemporaryAccessUntilUtc = null;
            student.BypassFilters = false;
            student.IsBlocked = student.TemporaryAccessPreviousBlockedState;
            student.TemporaryAccessPreviousBlockedState = false;
            _db.Students.Update(student);
            InvalidateStatsCache();
            Publish("StudentUpdated", MaterializeForDashboard(student));
        }

        return student;
    }

    public bool IsStudentBypassFilters(string ip)
    {
        var student = GetStudentByIpAndExpireTemporaryAccess(ip);
        return student?.BypassFilters ?? false;
    }

    public bool IsStudentTemporaryAccessActive(string ip)
    {
        var student = GetStudentByIpAndExpireTemporaryAccess(ip);
        return student != null && student.HasTemporaryAccess;
    }

    private void SetAllBypassFilters(bool bypass)
    {
        var all = _db.Students.FindAll().ToList();
        foreach (var s in all)
        {
            s.BypassFilters = bypass;
            if (bypass) s.IsBlocked = false;
            _db.Students.Update(s);
        }
        InvalidateStatsCache();
        Publish("AllStudentsUpdated", new { bypassFilters = bypass });
    }

    public bool IsStudentBlocked(string ip)
    {
        var student = FindStudentByIp(ip);
        return student?.IsBlocked ?? false;
    }

    public List<StudentInfo> GetAll()
    {
        CleanupPresence();
        var students = _db.Students.FindAll().ToList();
        var changed = false;

        foreach (var student in students)
        {
            if (student.TemporaryAccessUntilUtc.HasValue && student.TemporaryAccessUntilUtc.Value <= DateTime.UtcNow)
            {
                student.TemporaryAccessUntilUtc = null;
                student.BypassFilters = false;
                student.IsBlocked = student.TemporaryAccessPreviousBlockedState;
                student.TemporaryAccessPreviousBlockedState = false;
                _db.Students.Update(student);
                changed = true;
            }
        }

        if (changed) InvalidateStatsCache();

        return students.Select(MaterializeForDashboard).ToList();
    }

    public void MarkDisconnected(string ip)
    {
        var normalizedIp = IpAddressNormalizer.Normalize(ip);
        if (string.IsNullOrWhiteSpace(normalizedIp))
        {
            return;
        }

        _presenceByIp.TryRemove(normalizedIp, out _);
        var student = FindStudentByIp(normalizedIp);
        if (student != null)
        {
            student.IsConnected = false;
            _db.Students.Update(student);
            InvalidateStatsCache();
            Publish("StudentUpdated", MaterializeForDashboard(student));
        }
    }

    public DashboardStats GetStats()
    {
        // Try cache first (2 second window)
        if (_cache.TryGetValue(StatsCacheKey, out DashboardStats? cached) && cached != null)
        {
            return cached;
        }

        CleanupPresence();

        var students = _db.Students.FindAll().ToList();
        var changed = false;
        foreach (var student in students)
        {
            if (student.TemporaryAccessUntilUtc.HasValue && student.TemporaryAccessUntilUtc.Value <= DateTime.UtcNow)
            {
                student.TemporaryAccessUntilUtc = null;
                student.BypassFilters = false;
                student.IsBlocked = student.TemporaryAccessPreviousBlockedState;
                student.TemporaryAccessPreviousBlockedState = false;
                _db.Students.Update(student);
                changed = true;
            }
        }

        if (changed) InvalidateStatsCache();

        var dashboardStudents = students.Select(MaterializeForDashboard).ToList();
        var logs = _db.Logs.FindAll().ToList();
        var recent = logs.OrderByDescending(l => l.Timestamp).Take(20).ToList();

        var topDomains = logs
            .GroupBy(l => l.Domain)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new TopDomain { Domain = g.Key, Count = g.Count() })
            .ToList();

        var stats = new DashboardStats
        {
            TotalStudents = students.Count,
            ConnectedStudents = students.Count(s => s.IsConnected),
            BlockedStudents = students.Count(s => s.IsBlocked),
            TotalRequests = students.Sum(s => s.TotalRequests),
            BlockedRequests = students.Sum(s => s.BlockedRequests),
            BytesTransferred = students.Sum(s => s.BytesTransferred),
            TopDomains = topDomains,
            RecentActivity = recent.Select(l => new RecentActivity
            {
                StudentName = l.StudentName,
                Action = l.WasBlocked ? "Bloqueado" : "Acessou",
                Detail = l.Domain,
                Timestamp = l.Timestamp
            }).ToList()
        };

        _cache.Set(StatsCacheKey, stats, StatsCacheDuration);
        return stats;
    }

    private void InvalidateStatsCache()
    {
        _cache.Remove(StatsCacheKey);
    }

    private StudentInfo MaterializeForDashboard(StudentInfo source)
    {
        var lastSeen = ResolveLastSeen(source.IpAddress, source.LastSeen);
        return new StudentInfo
        {
            Id = source.Id,
            Name = source.Name,
            IpAddress = source.IpAddress,
            MacAddress = source.MacAddress,
            Hostname = source.Hostname,
            Os = source.Os,
            IsBlocked = source.IsBlocked,
            IsConnected = IsOnline(source.IpAddress, source.LastSeen),
            ConnectedAt = source.ConnectedAt,
            LastSeen = lastSeen,
            CurrentUrl = source.CurrentUrl,
            Group = source.Group,
            BypassFilters = source.BypassFilters,
            TemporaryAccessUntilUtc = source.TemporaryAccessUntilUtc,
            TotalRequests = source.TotalRequests,
            BlockedRequests = source.BlockedRequests,
            BytesTransferred = source.BytesTransferred
        };
    }

    private void TouchPresence(string ip)
    {
        var normalizedIp = IpAddressNormalizer.Normalize(ip);
        if (string.IsNullOrWhiteSpace(normalizedIp))
        {
            return;
        }

        // ConcurrentDictionary lock-free update
        _presenceByIp[normalizedIp] = DateTime.UtcNow;
    }

    private DateTime ResolveLastSeen(string ip, DateTime fallback)
    {
        var normalizedIp = IpAddressNormalizer.Normalize(ip);
        if (_presenceByIp.TryGetValue(normalizedIp, out var seen) && seen > fallback)
        {
            return seen;
        }

        return fallback;
    }

    private bool IsOnline(string ip, DateTime fallbackLastSeen)
    {
        var now = DateTime.UtcNow;
        var normalizedIp = IpAddressNormalizer.Normalize(ip);

        if (_presenceByIp.TryGetValue(normalizedIp, out var seen))
        {
            return (now - seen) <= OnlineWindow;
        }

        return (now - fallbackLastSeen) <= OnlineWindow;
    }

    private void CleanupPresence()
    {
        var now = DateTime.UtcNow;

        // Lock-free cleanup usando ConcurrentDictionary
        var stale = _presenceByIp
            .Where(kvp => (now - kvp.Value) > OnlineWindow)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var ip in stale)
        {
            _presenceByIp.TryRemove(ip, out _);
        }
    }

    private StudentInfo? FindStudentByIp(string ipAddress)
    {
        var normalizedIp = IpAddressNormalizer.Normalize(ipAddress);
        if (string.IsNullOrWhiteSpace(normalizedIp))
        {
            return null;
        }

        var student = _db.Students.FindOne(s => s.IpAddress == normalizedIp);
        if (student != null)
        {
            return student;
        }

        student = _db.Students.FindAll()
            .FirstOrDefault(s => IpAddressNormalizer.EqualsNormalized(s.IpAddress, normalizedIp));

        if (student != null &&
            !string.Equals(student.IpAddress, normalizedIp, StringComparison.OrdinalIgnoreCase))
        {
            student.IpAddress = normalizedIp;
            _db.Students.Update(student);
        }

        return student;
    }
}
