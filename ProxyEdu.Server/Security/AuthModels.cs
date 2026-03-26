namespace ProxyEdu.Server.Security;

public enum DashboardUserRole
{
    Administrator = 0,
    Professor = 1
}

public class DashboardUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public DashboardUserRole Role { get; set; } = DashboardUserRole.Professor;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }
}

public sealed class AuthenticatedUser
{
    public string Id { get; init; } = "";
    public string Username { get; init; } = "";
    public DashboardUserRole Role { get; init; }
}
