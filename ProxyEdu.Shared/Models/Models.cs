namespace ProxyEdu.Shared.Models;

public class StudentInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Os { get; set; } = "";
    public bool IsBlocked { get; set; } = false;
    public bool IsConnected { get; set; } = false;
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public string CurrentUrl { get; set; } = "";
    public string Group { get; set; } = "default";
    public bool BypassFilters { get; set; } = false; // when true, ignores blacklist/whitelist rules
    public int TotalRequests { get; set; } = 0;
    public int BlockedRequests { get; set; } = 0;
    public long BytesTransferred { get; set; } = 0;
}

public class AccessLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = "";
    public string StudentName { get; set; } = "";
    public string Url { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Method { get; set; } = "";
    public int StatusCode { get; set; }
    public bool WasBlocked { get; set; }
    public string BlockReason { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public long ResponseSize { get; set; }
}

public class FilterRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Pattern { get; set; } = "";
    public FilterType Type { get; set; } = FilterType.Blacklist;
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string CreatedBy { get; set; } = "professor";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ApplyToGroup { get; set; }
    public string? ApplyToStudentId { get; set; }
}

public enum FilterType
{
    Whitelist,
    Blacklist
}

public class StudentGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3b82f6";
    public bool IsBlocked { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ProxySettings
{
    public int ProxyPort { get; set; } = 8888;
    public int DashboardPort { get; set; } = 5000;
    public bool WhitelistMode { get; set; } = false; // if true, only whitelist allowed
    public bool LogAllRequests { get; set; } = true;
    public int MaxLogRetentionDays { get; set; } = 30;
    public string WelcomeMessage { get; set; } = "Acesso bloqueado pelo professor.";
    public bool ShowBlockPage { get; set; } = true;
}

public class DashboardStats
{
    public int TotalStudents { get; set; }
    public int ConnectedStudents { get; set; }
    public int BlockedStudents { get; set; }
    public int TotalRequests { get; set; }
    public int BlockedRequests { get; set; }
    public long BytesTransferred { get; set; }
    public List<TopDomain> TopDomains { get; set; } = new();
    public List<RecentActivity> RecentActivity { get; set; } = new();
}

public class TopDomain
{
    public string Domain { get; set; } = "";
    public int Count { get; set; }
}

public class RecentActivity
{
    public string StudentName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ProxyClientConfig
{
    public string ServerIp { get; set; } = "";
    public int ProxyPort { get; set; } = 8888;
    public string StudentName { get; set; } = "";
    public string Group { get; set; } = "default";
}

// Server Health Monitoring Models
public class ServerHealthStats
{
    public double CpuUsagePercent { get; set; }
    public long MemoryUsageMB { get; set; }
    public long MemoryTotalMB { get; set; }
    public double MemoryUsagePercent { get; set; }
    public int ActiveConnections { get; set; }
    public long TotalRequestsProcessed { get; set; }
    public long NetworkBytesSent { get; set; }
    public long NetworkBytesReceived { get; set; }
    public long UptimeSeconds { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int ConnectedStudents { get; set; }
    public int TotalStudents { get; set; }
    public ProcessInfo ProcessInfo { get; set; } = new();
    public List<ServerAlert> Alerts { get; set; } = new();
}

public class ProcessInfo
{
    public int Id { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public long WorkingSetMB { get; set; }
    public long PrivateMemoryMB { get; set; }
}

public class ServerAlert
{
    public AlertType Type { get; set; }
    public string Message { get; set; } = "";
    public double Value { get; set; }
    public double Threshold { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum AlertType
{
    Info,
    Warning,
    Critical
}
