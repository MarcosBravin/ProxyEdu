namespace ProxyEdu.Server.Security;

public static class AuthHttpContextExtensions
{
    public static AuthenticatedUser? GetAuthenticatedUser(this HttpContext context)
    {
        if (context.Items.TryGetValue("AuthenticatedUser", out var value) && value is AuthenticatedUser user)
        {
            return user;
        }

        return null;
    }

    public static bool IsAdmin(this HttpContext context)
    {
        return context.GetAuthenticatedUser()?.Role == DashboardUserRole.Administrator;
    }
}
