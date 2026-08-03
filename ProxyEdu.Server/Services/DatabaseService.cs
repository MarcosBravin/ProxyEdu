using LiteDB;
using ProxyEdu.Server.Security;
using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Services;

public class DatabaseService : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILogger<DatabaseService> _logger;
    private readonly string _defaultAdminPassword;
    private readonly RuntimeDiagnostics _diagnostics;

    public DatabaseService(ILogger<DatabaseService> logger, IConfiguration configuration, RuntimeDiagnostics diagnostics)
    {
        _logger = logger;
        _diagnostics = diagnostics;
        _defaultAdminPassword = configuration["Security:DefaultAdminPassword"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_defaultAdminPassword))
        {
            _defaultAdminPassword = "admin123";
            _logger.LogWarning("Usando senha padrão 'admin123' para admin. Configure 'Security:DefaultAdminPassword' no appsettings.json para maior segurança.");
        }

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ProxyEdu", "data.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _db = new LiteDatabase(dbPath);
        InitializeCollections();
    }

    private void InitializeCollections()
    {
        var settings = _db.GetCollection<ProxySettings>("settings");
        if (!settings.FindAll().Any())
        {
            settings.Insert(new ProxySettings());
        }

        var groups = _db.GetCollection<StudentGroup>("groups");
        if (!groups.FindAll().Any())
        {
            groups.Insert(new StudentGroup { Name = "Turma A", Color = "#3b82f6" });
            groups.Insert(new StudentGroup { Name = "Turma B", Color = "#22c55e" });
        }

        var users = _db.GetCollection<DashboardUser>("users");
        users.EnsureIndex(u => u.Username, unique: true);
        if (!users.FindAll().Any())
        {
            var (hash, salt) = PasswordHasher.HashPassword(_defaultAdminPassword);
            users.Insert(new DashboardUser
            {
                Username = "admin",
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = DashboardUserRole.Administrator,
                IsActive = true,
                IsPasswordChangeRequired = true,
                CreatedAtUtc = DateTime.UtcNow
            });
            _logger.LogWarning("Usuário admin criado com senha padrão. Troca de senha será exigida no primeiro login.");
        }

        // Ensure indexes for efficient queries (Phase 2 performance)
        var logs = _db.GetCollection<AccessLog>("logs");
        logs.EnsureIndex(l => l.Timestamp);
        logs.EnsureIndex(l => l.StudentId);
        logs.EnsureIndex(l => l.Domain);

        var students = _db.GetCollection<StudentInfo>("students");
        students.EnsureIndex(s => s.IpAddress);
        students.EnsureIndex(s => s.Group);
        students.EnsureIndex(s => s.IsConnected);
    }

    // Students
    public ILiteCollection<StudentInfo> Students => _db.GetCollection<StudentInfo>("students");

    // Logs
    public ILiteCollection<AccessLog> Logs => _db.GetCollection<AccessLog>("logs");

    // Filter Rules
    public ILiteCollection<FilterRule> FilterRules => _db.GetCollection<FilterRule>("filter_rules");

    // Groups
    public ILiteCollection<StudentGroup> Groups => _db.GetCollection<StudentGroup>("groups");

    // Settings
    public ProxySettings GetSettings()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try { return _db.GetCollection<ProxySettings>("settings").FindAll().First(); }
        finally { _diagnostics.RecordLiteRead(stopwatch.Elapsed); }
    }

    public void SaveSettings(ProxySettings settings)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var col = _db.GetCollection<ProxySettings>("settings");
        col.DeleteAll();
        col.Insert(settings);
        _diagnostics.RecordLiteWrite(stopwatch.Elapsed, 2);
    }

    #region Log Queries (Efficient)

    /// <summary>
    /// Busca logs paginada com filtros eficientes usando índices do LiteDB.
    /// </summary>
    public (List<AccessLog> Items, int Total) QueryLogs(
        string? studentId = null,
        string? domain = null,
        bool? blocked = null,
        int page = 1,
        int pageSize = 50)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var col = Logs;

        // Build query with indexed fields first
        IEnumerable<AccessLog> query;

        if (!string.IsNullOrEmpty(studentId))
        {
            // Use indexed lookup first
            var studentLogs = col.Find(l => l.StudentId == studentId);
            query = ApplyFilters(studentLogs, domain, blocked);
        }
        else if (!string.IsNullOrEmpty(domain))
        {
            query = col.Find(l => l.Domain.Contains(domain));
            query = ApplyFilters(query, null, blocked);
        }
        else if (blocked.HasValue)
        {
            query = col.Find(l => l.WasBlocked == blocked.Value);
        }
        else
        {
            // Use index for timestamp ordering
            query = col.Find(Query.All(), 0, int.MaxValue);
        }

        var total = query.Count();
        var items = query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        _diagnostics.RecordLiteRead(stopwatch.Elapsed);
        return (items, total);
    }

    private static IEnumerable<AccessLog> ApplyFilters(
        IEnumerable<AccessLog> source,
        string? domain,
        bool? blocked)
    {
        var result = source;
        if (!string.IsNullOrEmpty(domain))
            result = result.Where(l => l.Domain != null && l.Domain.Contains(domain, StringComparison.OrdinalIgnoreCase));
        if (blocked.HasValue)
            result = result.Where(l => l.WasBlocked == blocked.Value);
        return result;
    }

    #endregion

    public void AddLog(AccessLog log)
    {
        Logs.Insert(log);
        // Clean old logs
        var settings = GetSettings();
        var cutoff = DateTime.UtcNow.AddDays(-settings.MaxLogRetentionDays);
        Logs.DeleteMany(l => l.Timestamp < cutoff);
    }

    public void Dispose() => _db.Dispose();

    // Dashboard users
    public ILiteCollection<DashboardUser> Users => _db.GetCollection<DashboardUser>("users");
}
