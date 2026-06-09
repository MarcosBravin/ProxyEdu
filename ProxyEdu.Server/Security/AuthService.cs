using ProxyEdu.Server.Services;

namespace ProxyEdu.Server.Security;

public class AuthService
{
    private static readonly TimeSpan LastLoginUpdateInterval = TimeSpan.FromMinutes(5);
    private readonly DatabaseService _db;

    public AuthService(DatabaseService db)
    {
        _db = db;
    }

    public AuthenticatedUser? Validate(string username, string password)
    {
        var user = _db.Users.FindOne(u => u.Username == username);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        if (!PasswordHasher.VerifyPassword(password, user.PasswordHash, user.PasswordSalt))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        if (user.LastLoginAtUtc is null || now - user.LastLoginAtUtc.Value >= LastLoginUpdateInterval)
        {
            user.LastLoginAtUtc = now;
            _db.Users.Update(user);
        }

        return new AuthenticatedUser
        {
            Id = user.Id,
            Username = user.Username,
            Role = user.Role
        };
    }

    public IEnumerable<DashboardUser> ListUsers()
    {
        return _db.Users.FindAll().OrderBy(u => u.Username);
    }

    public DashboardUser CreateUser(string username, string password, DashboardUserRole role)
    {
        username = NormalizeUsername(username);
        ValidatePassword(password);

        if (_db.Users.Exists(u => u.Username == username))
        {
            throw new InvalidOperationException("Usuario ja existe.");
        }

        var (hash, salt) = PasswordHasher.HashPassword(password);
        var user = new DashboardUser
        {
            Username = username,
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = role,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Users.Insert(user);
        return user;
    }

    public DashboardUser UpdateUser(string id, string? username, string? password, DashboardUserRole? role, bool? isActive)
    {
        var user = _db.Users.FindById(id) ?? throw new InvalidOperationException("Usuario nao encontrado.");

        if (!string.IsNullOrWhiteSpace(username) && !string.Equals(username, user.Username, StringComparison.Ordinal))
        {
            username = NormalizeUsername(username);
            if (_db.Users.Exists(u => u.Username == username && u.Id != id))
            {
                throw new InvalidOperationException("Nome de usuario ja em uso.");
            }

            user.Username = username;
        }

        if (!string.IsNullOrWhiteSpace(password))
        {
            ValidatePassword(password);
            var (hash, salt) = PasswordHasher.HashPassword(password);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
        }

        if (role.HasValue)
        {
            EnsureAtLeastOneActiveAdminAfterChange(user.Id, role.Value, isActive ?? user.IsActive);
            user.Role = role.Value;
        }

        if (isActive.HasValue)
        {
            EnsureAtLeastOneActiveAdminAfterChange(user.Id, role ?? user.Role, isActive.Value);
            user.IsActive = isActive.Value;
        }

        _db.Users.Update(user);
        return user;
    }

    public void DeleteUser(string id)
    {
        var user = _db.Users.FindById(id) ?? throw new InvalidOperationException("Usuario nao encontrado.");
        if (user.Role == DashboardUserRole.Administrator && user.IsActive)
        {
            var otherActiveAdmins = _db.Users.Count(u =>
                u.Id != id &&
                u.IsActive &&
                u.Role == DashboardUserRole.Administrator);
            if (otherActiveAdmins == 0)
            {
                throw new InvalidOperationException("Nao e permitido remover o ultimo administrador ativo.");
            }
        }

        _db.Users.Delete(id);
    }

    private void EnsureAtLeastOneActiveAdminAfterChange(string currentUserId, DashboardUserRole newRole, bool newIsActive)
    {
        if (newRole == DashboardUserRole.Administrator && newIsActive)
        {
            return;
        }

        var otherActiveAdmins = _db.Users.Count(u =>
            u.Id != currentUserId &&
            u.IsActive &&
            u.Role == DashboardUserRole.Administrator);

        if (otherActiveAdmins == 0)
        {
            throw new InvalidOperationException("A operacao deixaria o sistema sem administrador ativo.");
        }
    }

    private static string NormalizeUsername(string username)
    {
        username = username.Trim();
        if (username.Length < 3)
        {
            throw new InvalidOperationException("Usuario deve ter pelo menos 3 caracteres.");
        }

        return username;
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 8)
        {
            throw new InvalidOperationException("Senha deve ter pelo menos 8 caracteres.");
        }
    }
}
