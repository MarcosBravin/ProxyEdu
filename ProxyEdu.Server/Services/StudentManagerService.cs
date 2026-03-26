using ProxyEdu.Shared.Models;
using Microsoft.AspNetCore.SignalR;
using ProxyEdu.Server.Hubs;

namespace ProxyEdu.Server.Services;

public class StudentManagerService
{
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(20);

    private readonly DatabaseService _db;
    private readonly IHubContext<ProxyHub> _hub;
    private readonly Dictionary<string, DateTime> _presenceByIp = new();
    private readonly object _lock = new();

    public StudentManagerService(DatabaseService db, IHubContext<ProxyHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public StudentInfo RegisterOrUpdate(string ip, string hostname, string name, string os, string macAddress, string group)
    {
        lock (_lock)
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

            var snapshot = MaterializeForDashboard(existing);
            _hub.Clients.All.SendAsync("StudentUpdated", snapshot);
            return snapshot;
        }
    }

    public void TouchHeartbeat(string ip, string? currentUrl = null)
    {
        lock (_lock)
        {
            var normalizedIp = IpAddressNormalizer.Normalize(ip);
            if (string.IsNullOrWhiteSpace(normalizedIp)) return;
            TouchPresence(normalizedIp);

            var student = FindStudentByIp(normalizedIp);
            if (student == null) return;

            if (!string.IsNullOrWhiteSpace(currentUrl))
            {
                _hub.Clients.All.SendAsync("StudentActivity", new
                {
                    studentId = student.Id,
                    ip = normalizedIp,
                    url = currentUrl,
                    blocked = false,
                    timestamp = DateTime.UtcNow
                });
            }
        }
    }

    public void UpdateActivity(string ip, string url, bool blocked, long bytes)
    {
        lock (_lock)
        {
            var normalizedIp = IpAddressNormalizer.Normalize(ip);
            if (string.IsNullOrWhiteSpace(normalizedIp)) return;

            TouchPresence(normalizedIp);

            var student = FindStudentByIp(normalizedIp);
            if (student == null) return;

            student.LastSeen = DateTime.UtcNow;
            student.CurrentUrl = url;
            student.TotalRequests++;
            student.BytesTransferred += bytes;
            if (blocked) student.BlockedRequests++;
            _db.Students.Update(student);

            _hub.Clients.All.SendAsync("StudentActivity", new
            {
                studentId = student.Id,
                ip = normalizedIp,
                url,
                blocked,
                timestamp = DateTime.UtcNow
            });
        }
    }

    public void SetStudentBlocked(string studentId, bool blocked)
    {
        lock (_lock)
        {
            var student = _db.Students.FindById(studentId);
            if (student == null) return;
            student.IsBlocked = blocked;
            if (blocked) student.BypassFilters = false;
            _db.Students.Update(student);

            _hub.Clients.All.SendAsync("StudentUpdated", MaterializeForDashboard(student));
        }
    }

    public void SetGroupBlocked(string groupName, bool blocked)
    {
        lock (_lock)
        {
            var students = _db.Students.Find(s => s.Group == groupName).ToList();
            foreach (var s in students)
            {
                s.IsBlocked = blocked;
                if (blocked) s.BypassFilters = false;
                _db.Students.Update(s);
            }

            _hub.Clients.All.SendAsync("GroupUpdated", new { group = groupName, blocked });
        }
    }

    public void BlockAll() => SetAllBlocked(true);
    public void UnblockAll() => SetAllBlocked(false);
    public void ReleaseAllSitesForAll() => SetAllBypassFilters(true);
    public void RestoreFiltersForAll() => SetAllBypassFilters(false);

    private void SetAllBlocked(bool blocked)
    {
        lock (_lock)
        {
            var all = _db.Students.FindAll().ToList();
            foreach (var s in all)
            {
                s.IsBlocked = blocked;
                if (blocked) s.BypassFilters = false;
                _db.Students.Update(s);
            }
            _hub.Clients.All.SendAsync("AllStudentsUpdated", new { blocked });
        }
    }

    public void SetStudentBypassFilters(string studentId, bool bypass)
    {
        lock (_lock)
        {
            var student = _db.Students.FindById(studentId);
            if (student == null) return;
            student.BypassFilters = bypass;
            if (bypass) student.IsBlocked = false;
            _db.Students.Update(student);
            _hub.Clients.All.SendAsync("StudentUpdated", MaterializeForDashboard(student));
        }
    }

    public bool IsStudentBypassFilters(string ip)
    {
        lock (_lock)
        {
            var student = FindStudentByIp(ip);
            return student?.BypassFilters ?? false;
        }
    }

    private void SetAllBypassFilters(bool bypass)
    {
        lock (_lock)
        {
            var all = _db.Students.FindAll().ToList();
            foreach (var s in all)
            {
                s.BypassFilters = bypass;
                if (bypass) s.IsBlocked = false;
                _db.Students.Update(s);
            }
            _hub.Clients.All.SendAsync("AllStudentsUpdated", new { bypassFilters = bypass });
        }
    }

    public bool IsStudentBlocked(string ip)
    {
        lock (_lock)
        {
            var student = FindStudentByIp(ip);
            return student?.IsBlocked ?? false;
        }
    }

    public List<StudentInfo> GetAll()
    {
        lock (_lock)
        {
            CleanupPresence();
            return _db.Students.FindAll().Select(MaterializeForDashboard).ToList();
        }
    }

    public void MarkDisconnected(string ip)
    {
        lock (_lock)
        {
            var normalizedIp = IpAddressNormalizer.Normalize(ip);
            if (string.IsNullOrWhiteSpace(normalizedIp))
            {
                return;
            }

            _presenceByIp.Remove(normalizedIp);
            var student = FindStudentByIp(normalizedIp);
            if (student != null)
            {
                student.IsConnected = false;
                _db.Students.Update(student);
                _hub.Clients.All.SendAsync("StudentUpdated", MaterializeForDashboard(student));
            }
        }
    }

    public DashboardStats GetStats()
    {
        lock (_lock)
        {
            CleanupPresence();

            var students = _db.Students.FindAll().Select(MaterializeForDashboard).ToList();
            var logs = _db.Logs.FindAll().ToList();
            var recent = logs.OrderByDescending(l => l.Timestamp).Take(20).ToList();

            var topDomains = logs
                .GroupBy(l => l.Domain)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => new TopDomain { Domain = g.Key, Count = g.Count() })
                .ToList();

            return new DashboardStats
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
        }
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
        var stale = _presenceByIp
            .Where(kvp => (now - kvp.Value) > OnlineWindow)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var ip in stale)
        {
            _presenceByIp.Remove(ip);
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
