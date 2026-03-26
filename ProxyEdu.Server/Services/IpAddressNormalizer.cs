using System.Net;

namespace ProxyEdu.Server.Services;

public static class IpAddressNormalizer
{
    public static string Normalize(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return string.Empty;
        }

        var trimmed = ipAddress.Trim();
        if (IPAddress.TryParse(trimmed, out var parsed))
        {
            if (parsed.IsIPv4MappedToIPv6)
            {
                parsed = parsed.MapToIPv4();
            }

            return parsed.ToString();
        }

        // Remove zone index on link-local IPv6 if present (fe80::1%12 -> fe80::1).
        var zoneSeparator = trimmed.IndexOf('%');
        if (zoneSeparator > 0)
        {
            return trimmed[..zoneSeparator];
        }

        return trimmed;
    }

    public static bool EqualsNormalized(string? first, string? second)
    {
        return string.Equals(
            Normalize(first),
            Normalize(second),
            StringComparison.OrdinalIgnoreCase);
    }
}
