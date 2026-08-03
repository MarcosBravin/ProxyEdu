using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace ProxyEdu.Shared.Utils;

/// <summary>
/// Utility class for managing Windows proxy settings via Registry and WinINet API.
/// Centralizes all proxy configuration logic used by both ProxyClientService and ProxyProtectionService.
/// </summary>
public static class WindowsProxyManager
{
    [DllImport("wininet.dll")]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    /// <summary>
    /// Configura o proxy do Windows para todos os usuários.
    /// </summary>
    public static void SetProxy(string proxyAddress, bool enable)
    {
        ApplyToAllLoadedUsers(proxyAddress, enable);
        ApplyToCurrentUser(proxyAddress, enable);
        RefreshInternetSettings();
    }

    /// <summary>
    /// Obtém as configurações atuais de proxy do usuário atual.
    /// </summary>
    public static (string? proxyAddress, bool enabled) GetCurrentProxySettings()
    {
        try
        {
            using var regKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");

            if (regKey is null)
                return (null, false);

            var proxyServer = regKey.GetValue("ProxyServer") as string;
            var proxyEnable = regKey.GetValue("ProxyEnable") as int? ?? 0;

            return (proxyServer, proxyEnable == 1);
        }
        catch
        {
            return (null, false);
        }
    }

    /// <summary>
    /// Verifica se um SID é de usuário real (não serviço/sistema).
    /// </summary>
    private static bool IsUserSid(string sid)
    {
        return sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) ||
               sid.StartsWith("S-1-12-1-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Aplica configuração de proxy ao usuário atual via Registry.
    /// </summary>
    private static void ApplyToCurrentUser(string proxyAddress, bool enable)
    {
        try
        {
            using var regKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);

            if (regKey is null) return;

            ApplyRegistryValues(regKey, proxyAddress, enable);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro ao aplicar proxy ao usuário atual: {ex.Message}");
        }
    }

    /// <summary>
    /// Aplica configuração de proxy a todos os usuários logados via Registry.Users.
    /// </summary>
    private static void ApplyToAllLoadedUsers(string proxyAddress, bool enable)
    {
        try
        {
            using var usersRoot = Registry.Users;
            foreach (var sid in usersRoot.GetSubKeyNames())
            {
                if (!IsUserSid(sid)) continue;

                try
                {
                    using var regKey = usersRoot.OpenSubKey(
                        $@"{sid}\Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);

                    if (regKey is null) continue;

                    ApplyRegistryValues(regKey, proxyAddress, enable);
                }
                catch
                {
                    // Permissão negada para alguns SIDs é normal
                }
            }
        }
        catch
        {
            // Erro ao acessar usuários
        }
    }

    /// <summary>
    /// Aplica os valores de proxy em uma chave de registro específica.
    /// Remove configurações de PAC e AutoDetect quando habilitando proxy.
    /// </summary>
    private static void ApplyRegistryValues(RegistryKey regKey, string proxyAddress, bool enable)
    {
        if (enable)
        {
            regKey.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            regKey.SetValue("ProxyServer", proxyAddress, RegistryValueKind.String);
            regKey.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;192.168.*;<local>", RegistryValueKind.String);

            // Remover configurações de PAC que podem causar conflito
            try { regKey.DeleteValue("AutoConfigURL", false); } catch { }
            try { regKey.DeleteValue("AutoConfigDetect", false); } catch { }
        }
        else
        {
            regKey.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
        }
    }

    /// <summary>
    /// Força o refresh das configurações de internet no Windows via WinINet API.
    /// Necessário para que as alterações no Registry tenham efeito imediato nos navegadores.
    /// </summary>
    private static void RefreshInternetSettings()
    {
        try
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch
        {
            // Falha no refresh não é crítica
        }
    }
}
