using ProxyEdu.Server.Services;

namespace ProxyEdu.Server.Security;

public class AuthService
{
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

        user.LastLoginAtUtc = DateTime.UtcNow;
        _db.Users.Update(user);

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
            if (_db.Users.Exists(u => u.Username == username && u.Id != id))
            {
                throw new InvalidOperationException("Nome de usuario ja em uso.");
            }

            user.Username = username.Trim();
        }

        if (!string.IsNullOrWhiteSpace(password))
        {
            var (hash, salt) = PasswordHasher.HashPassword(password);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
        }

        if (role.HasValue)
        {
            user.Role = role.Value;
        }

        if (isActive.HasValue)
        {
            user.IsActive = isActive.Value;
        }

        _db.Users.Update(user);
        return user;
    }

    public void DeleteUser(string id)
    {
        _db.Users.Delete(id);
    }
}
