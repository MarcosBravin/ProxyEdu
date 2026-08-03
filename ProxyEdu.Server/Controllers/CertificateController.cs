using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Services;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/certificate")]
public class CertificateController : ControllerBase
{
    private readonly ProxyServerService _proxyServerService;
    private readonly ILogger<CertificateController> _logger;

    // Simple in-memory rate limiting: max 10 requests per minute per IP
    private static readonly ConcurrentDictionary<string, List<DateTime>> _requestTimestamps = new();
    private static readonly int RateLimitMaxRequests = 10;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

    public CertificateController(ProxyServerService proxyServerService, ILogger<CertificateController> logger)
    {
        _proxyServerService = proxyServerService;
        _logger = logger;
    }

    [HttpGet("root")]
    public IActionResult GetRootCertificate()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;

        // Rate limiting check
        if (!IsRateLimited(remoteIp))
        {
            _logger.LogWarning("Rate limit excedido para certificado root. IP: {RemoteIp}", remoteIp);
            return StatusCode(429, new { error = "Muitas requisições. Tente novamente em 1 minuto." });
        }

        // Restrict to private network only (clients must be on same LAN)
        if (remoteIp != null && !IsPrivateNetwork(remoteIp))
        {
            _logger.LogWarning("Tentativa de acesso externo ao certificado root. IP: {RemoteIp}", remoteIp);
            return Forbid();
        }

        var cert = _proxyServerService.GetRootCertificate();
        if (cert is null)
        {
            return NotFound(new { message = "Certificado raiz ainda não foi inicializado." });
        }

        var certBytes = cert.Export(X509ContentType.Cert);
        return File(certBytes, "application/x-x509-ca-cert", "proxyedu-root.cer");
    }

    private bool IsRateLimited(IPAddress? remoteIp)
    {
        if (remoteIp == null) return false;

        var ipKey = remoteIp.ToString();
        var now = DateTime.UtcNow;

        var timestamps = _requestTimestamps.GetOrAdd(ipKey, _ => new List<DateTime>());

        lock (timestamps)
        {
            // Remove timestamps outside the window
            timestamps.RemoveAll(t => (now - t) > RateLimitWindow);

            if (timestamps.Count >= RateLimitMaxRequests)
            {
                return false;
            }

            timestamps.Add(now);
            return true;
        }
    }

    private static bool IsPrivateNetwork(IPAddress ip)
    {
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // Allow IPv6 localhost
            if (IPAddress.IsLoopback(ip)) return true;
            return false;
        }

        var bytes = ip.GetAddressBytes();
        if (bytes[0] == 10) return true;
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
        if (bytes[0] == 192 && bytes[1] == 168) return true;
        if (bytes[0] == 169 && bytes[1] == 254) return true;
        if (bytes[0] == 127) return true;

        return false;
    }
}
