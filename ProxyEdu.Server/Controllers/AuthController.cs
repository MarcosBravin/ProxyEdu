using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Security;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    [HttpGet("me")]
    public IActionResult Me()
    {
        var user = HttpContext.GetAuthenticatedUser();
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(new
        {
            id = user.Id,
            username = user.Username,
            role = user.Role.ToString()
        });
    }
}

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly AuthService _authService;

    public UsersController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpGet]
    public IActionResult List()
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        var users = _authService.ListUsers().Select(u => new
        {
            id = u.Id,
            username = u.Username,
            role = u.Role.ToString(),
            isActive = u.IsActive,
            createdAtUtc = u.CreatedAtUtc,
            lastLoginAtUtc = u.LastLoginAtUtc
        });

        return Ok(users);
    }

    [HttpPost]
    public IActionResult Create([FromBody] CreateUserRequest request)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Username e password sao obrigatorios.");
        }

        if (!Enum.TryParse<DashboardUserRole>(request.Role, ignoreCase: true, out var role))
        {
            return BadRequest("Role invalida. Use Administrator ou Professor.");
        }

        try
        {
            var user = _authService.CreateUser(request.Username.Trim(), request.Password, role);
            return Ok(new
            {
                id = user.Id,
                username = user.Username,
                role = user.Role.ToString(),
                isActive = user.IsActive
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id}")]
    public IActionResult Update(string id, [FromBody] UpdateUserRequest request)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        DashboardUserRole? parsedRole = null;
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            if (!Enum.TryParse<DashboardUserRole>(request.Role, ignoreCase: true, out var role))
            {
                return BadRequest("Role invalida. Use Administrator ou Professor.");
            }

            parsedRole = role;
        }

        try
        {
            var user = _authService.UpdateUser(
                id,
                request.Username,
                request.Password,
                parsedRole,
                request.IsActive);

            return Ok(new
            {
                id = user.Id,
                username = user.Username,
                role = user.Role.ToString(),
                isActive = user.IsActive
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        var current = HttpContext.GetAuthenticatedUser();
        if (current is not null && string.Equals(current.Id, id, StringComparison.Ordinal))
        {
            return BadRequest("Nao e permitido remover o proprio usuario logado.");
        }

        _authService.DeleteUser(id);
        return Ok(new { success = true });
    }
}

public sealed class CreateUserRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "Professor";
}

public sealed class UpdateUserRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Role { get; set; }
    public bool? IsActive { get; set; }
}
