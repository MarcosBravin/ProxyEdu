using LiteDB;
using ProxyEdu.Server.Security;
using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Services;

public class DatabaseService : IDisposable
{
    private readonly LiteDatabase _db;

    public DatabaseService()
    {
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
            var (hash, salt) = PasswordHasher.HashPassword("admin123");
            users.Insert(new DashboardUser
            {
                Username = "admin",
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = DashboardUserRole.Administrator,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
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
        return _db.GetCollection<ProxySettings>("settings").FindAll().First();
    }

    public void SaveSettings(ProxySettings settings)
    {
        var col = _db.GetCollection<ProxySettings>("settings");
        col.DeleteAll();
        col.Insert(settings);
    }

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
