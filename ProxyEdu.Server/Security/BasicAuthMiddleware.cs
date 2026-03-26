using System.Text;

namespace ProxyEdu.Server.Security;

public class BasicAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthService _authService;

    public BasicAuthMiddleware(RequestDelegate next, AuthService authService)
    {
        _next = next;
        _authService = authService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsAnonymousPath(context.Request.Path) || HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (!TryReadCredentials(context.Request, out var username, out var password) ||
            username is null ||
            password is null)
        {
            Challenge(context);
            return;
        }

        var authenticatedUser = _authService.Validate(username, password);
        if (authenticatedUser is null)
        {
            Challenge(context);
            return;
        }

        context.Items["AuthenticatedUser"] = authenticatedUser;
        await _next(context);
    }

    private static void Challenge(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        // Avoid browser-native Basic Auth popup for SPA API/HUB calls.
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/hub", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        context.Response.Headers.WWWAuthenticate = "Basic realm=\"ProxyEdu Dashboard\", charset=\"UTF-8\"";
    }

    private static bool TryReadCredentials(HttpRequest request, out string? username, out string? password)
    {
        username = null;
        password = null;

        if (!request.Headers.TryGetValue("Authorization", out var authorizationValues))
        {
            return false;
        }

        var raw = authorizationValues.ToString();
        if (!raw.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var encoded = raw["Basic ".Length..].Trim();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var separator = decoded.IndexOf(':');
            if (separator <= 0)
            {
                return false;
            }

            username = decoded[..separator];
            password = decoded[(separator + 1)..];
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAnonymousPath(PathString path)
    {
        // Public dashboard files (custom login is handled in the SPA).
        // Only API/HUB paths are protected by this middleware.
        if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWithSegments("/hub", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWithSegments("/api/students/register", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/api/students/heartbeat", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/api/certificate/root", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/hub", StringComparison.OrdinalIgnoreCase);
    }
}
