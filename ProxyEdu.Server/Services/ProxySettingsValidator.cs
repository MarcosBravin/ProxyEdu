using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Services;

public static class ProxySettingsValidator
{
    public static string? Validate(ProxySettings settings)
    {
        if (settings.ProxyPort is < 1 or > 65535) return "ProxyPort deve estar entre 1 e 65535.";
        if (settings.DashboardPort is < 1 or > 65535) return "DashboardPort deve estar entre 1 e 65535.";
        if (settings.MaxLogRetentionDays is < 1 or > 3650) return "MaxLogRetentionDays deve estar entre 1 e 3650.";
        return null;
    }
}
