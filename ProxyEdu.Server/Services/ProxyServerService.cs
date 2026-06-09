using Microsoft.AspNetCore.SignalR;
using ProxyEdu.Server.Hubs;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;
using ProxyEdu.Server.Services;
using ProxyEdu.Shared.Models;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace ProxyEdu.Server.Services;

public class ProxyServerService : BackgroundService
{
    private const string CertDirectory = @"C:\ProgramData\ProxyEdu\certs";
    private const string RootPfxPath = @"C:\ProgramData\ProxyEdu\certs\proxyedu-root-ca.pfx";
    private const string RootPfxPassword = "ProxyEdu_Local_CA_2026";
    private const string RootCertificateName = "ProxyEdu Root CA";
    private const string RootCertificateIssuer = "ProxyEdu";

    private readonly ProxyServer _proxyServer;
    private readonly StudentManagerService _studentManager;
    private readonly FilterService _filterService;
    private readonly DatabaseService _db;
    private readonly IHubContext<ProxyHub> _hub;
    private readonly ILogger<ProxyServerService> _logger;
    private ExplicitProxyEndPoint? _explicitEndPoint;

    public ProxyServerService(
        StudentManagerService studentManager,
        FilterService filterService,
        DatabaseService db,
        IHubContext<ProxyHub> hub,
        ILogger<ProxyServerService> logger)
    {
        _studentManager = studentManager;
        _filterService = filterService;
        _db = db;
        _hub = hub;
        _logger = logger;
        _proxyServer = new ProxyServer();
        ConfigureCertificateManager();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = _db.GetSettings();

        _proxyServer.BeforeRequest += OnBeforeRequest;
        _proxyServer.BeforeResponse += OnBeforeResponse;
        _proxyServer.ServerCertificateValidationCallback += OnCertValidation;

        if (settings.EnableHttpsInspection)
        {
            EnsureRootCertificateTrusted();
        }

        _explicitEndPoint = new ExplicitProxyEndPoint(
            IPAddress.Any,
            settings.ProxyPort,
            decryptSsl: settings.EnableHttpsInspection);
        _explicitEndPoint.BeforeTunnelConnectRequest += OnBeforeTunnelConnectRequest;

        _proxyServer.AddEndPoint(_explicitEndPoint);
        _proxyServer.Start();

        _logger.LogInformation(
            "Proxy started on port {Port}. HTTPS mode: {HttpsMode}",
            settings.ProxyPort,
            settings.EnableHttpsInspection ? "inspection" : "tunnel");

        await Task.Delay(Timeout.Infinite, stoppingToken);

        _proxyServer.Stop();
    }

    public X509Certificate2? GetRootCertificate()
    {
        return _proxyServer.CertificateManager.RootCertificate;
    }

    public bool IsRootCertificateTrusted()
    {
        try
        {
            return _proxyServer.CertificateManager.RootCertificate != null &&
                   _proxyServer.CertificateManager.IsRootCertificateMachineTrusted();
        }
        catch
        {
            return false;
        }
    }

    private Task OnBeforeTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs e)
    {
        var clientIp = e.ClientRemoteEndPoint.Address.ToString();
        var (host, port) = ExtractConnectTarget(e);
        var url = $"https://{host}:{port}";
        var settings = _db.GetSettings();
        var certificateTrusted = IsRootCertificateTrusted();
        var canInspectHttps = settings.EnableHttpsInspection && certificateTrusted;

        e.DecryptSsl = canInspectHttps;
        _studentManager.TouchHeartbeat(clientIp, url);

        if (_studentManager.IsStudentBlocked(clientIp))
        {
            if (canInspectHttps)
            {
                return Task.CompletedTask;
            }

            DenyConnect(e, "Aluno bloqueado");
            _studentManager.UpdateActivity(clientIp, url, true, 0);
            LogAccess(clientIp, url, "CONNECT", true, "HTTPS CONNECT block: Aluno bloqueado");
            NotifyHttpsBlocked(clientIp, host, port, "Aluno bloqueado", "student-block", null);
            LogProxyDecision(clientIp, host, port, "HTTPS CONNECT", "student-block", null, false, "Aluno bloqueado");
            return Task.CompletedTask;
        }

        if (_studentManager.IsStudentBypassFilters(clientIp))
        {
            CountAllowedTunnelRequest(clientIp, url, canInspectHttps);
            LogAccess(clientIp, url, "CONNECT", false, "Liberacao total do aluno");
            LogProxyDecision(clientIp, host, port, "HTTPS CONNECT", "student-bypass", null, true, "Liberacao total do aluno");
            return Task.CompletedTask;
        }

        var decision = _filterService.EvaluateUrl(host, clientIp);
        if (!decision.IsAllowed)
        {
            if (canInspectHttps)
            {
                return Task.CompletedTask;
            }

            DenyConnect(e, decision.Reason);
            _studentManager.UpdateActivity(clientIp, url, true, 0);
            LogAccess(clientIp, url, "CONNECT", true, $"HTTPS CONNECT block: {decision.Reason}");
            NotifyHttpsBlocked(clientIp, host, port, decision.Reason, decision.Policy, decision.MatchedRule);
            LogProxyDecision(clientIp, host, port, "HTTPS CONNECT", decision.Policy, decision.MatchedRule, false, decision.Reason);
            return Task.CompletedTask;
        }

        CountAllowedTunnelRequest(clientIp, url, canInspectHttps);
        LogAccess(clientIp, url, "CONNECT", false, decision.Reason);
        LogProxyDecision(clientIp, host, port, "HTTPS CONNECT", decision.Policy, decision.MatchedRule, true, decision.Reason);
        return Task.CompletedTask;
    }

    private async Task OnBeforeRequest(object sender, SessionEventArgs e)
    {
        var clientIp = e.ClientRemoteEndPoint.Address.ToString();
        var url = e.HttpClient.Request.Url;
        var method = e.HttpClient.Request.Method;
        var requestType = e.HttpClient.Request.IsHttps ? "HTTPS inspection block" : "HTTP page block";
        var requestPort = e.HttpClient.Request.IsHttps ? 443 : 80;
        var isConnectTunnel = string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase);

        // CONNECT decisions are handled before opening the HTTPS tunnel.
        if (isConnectTunnel)
        {
            return;
        }

        _studentManager.TouchHeartbeat(clientIp, url);

        // Check if student is completely blocked
        if (_studentManager.IsStudentBlocked(clientIp))
        {
            await RedirectToBlockPage(
                e,
                _filterService.ExtractDomain(url),
                "Aluno bloqueado",
                "student-block",
                requestType,
                clientIp);
            _studentManager.UpdateActivity(clientIp, url, true, 0);
            LogAccess(clientIp, url, method, true, $"{requestType}: Aluno bloqueado");
            LogProxyDecision(clientIp, _filterService.ExtractDomain(url), requestPort, requestType, "student-block", null, false, "Aluno bloqueado");
            return;
        }

        // Explicit per-student bypass: ignore blacklist/whitelist filters
        if (_studentManager.IsStudentBypassFilters(clientIp))
        {
            LogAccess(clientIp, url, method, false, "Liberacao total do aluno");
            LogProxyDecision(clientIp, _filterService.ExtractDomain(url), requestPort, requestType, "student-bypass", null, true, "Liberacao total do aluno");
            return;
        }

        var decision = _filterService.EvaluateUrl(url, clientIp);
        if (!decision.IsAllowed)
        {
            await RedirectToBlockPage(e, decision.Domain, decision.Reason, decision.Policy, requestType, clientIp);
            _studentManager.UpdateActivity(clientIp, url, true, 0);
            LogAccess(clientIp, url, method, true, $"{requestType}: {decision.Reason}");
            LogProxyDecision(clientIp, decision.Domain, requestPort, requestType, decision.Policy, decision.MatchedRule, false, decision.Reason);
            return;
        }

        LogProxyDecision(clientIp, decision.Domain, requestPort, e.HttpClient.Request.IsHttps ? "HTTPS inspection allow" : "HTTP allow", decision.Policy, decision.MatchedRule, true, decision.Reason);
        // Allowed requests are counted in OnBeforeResponse, where response size is available.
    }

    private async Task OnBeforeResponse(object sender, SessionEventArgs e)
    {
        var clientIp = e.ClientRemoteEndPoint.Address.ToString();
        var size = e.HttpClient.Response.ContentLength;
        _studentManager.TouchHeartbeat(clientIp, e.HttpClient.Request.Url);
        _studentManager.UpdateActivity(clientIp, e.HttpClient.Request.Url, false, Math.Max(0, size));
    }

    private Task OnCertValidation(object sender, CertificateValidationEventArgs e)
    {
        e.IsValid = true;
        return Task.CompletedTask;
    }

    private Task RedirectToBlockPage(
        SessionEventArgs e,
        string domain,
        string reason,
        string policy,
        string blockType,
        string clientIp)
    {
        var student = ResolveStudent(clientIp);
        var blockKind = blockType.Contains("HTTPS", StringComparison.OrdinalIgnoreCase)
            ? "https"
            : "http";
        var destination = BuildBlockedPageDestination(domain, reason, policy, blockKind);
        var safeDestination = WebUtility.HtmlEncode(destination.Url);

        e.Ok($@"<!DOCTYPE html>
<html lang='pt-BR'>
<head>
<meta charset='UTF-8'>
<meta http-equiv='refresh' content='0; url={safeDestination}'>
<title>ProxyEdu - redirecionando</title>
</head>
<body>
  <p>Acesso bloqueado pelo ProxyEdu. Redirecionando para a pagina de aviso...</p>
  <p><a href='{safeDestination}'>Abrir pagina de aviso</a></p>
</body>
</html>");
        e.HttpClient.Response.StatusCode = 302;
        e.HttpClient.Response.StatusDescription = "ProxyEdu: redirecionando para aviso de bloqueio";
        e.HttpClient.Response.Headers.AddHeader("Location", destination.Url);

        _logger.LogInformation(
            "Blocked request redirected: host={Host} type={Type} destination={Destination} url={RedirectUrl} student={Student} ip={Ip}",
            domain,
            blockType,
            destination.Kind,
            destination.Url,
            student?.Name ?? "-",
            IpAddressNormalizer.Normalize(clientIp));

        return Task.CompletedTask;
    }

    private StudentInfo? ResolveStudent(string ip)
    {
        var normalizedIp = IpAddressNormalizer.Normalize(ip);
        return _db.Students.FindOne(s => s.IpAddress == normalizedIp)
            ?? _db.Students.FindAll().FirstOrDefault(s => IpAddressNormalizer.EqualsNormalized(s.IpAddress, normalizedIp));
    }

    private void LogAccess(string ip, string url, string method, bool blocked, string reason = "")
    {
        var normalizedIp = IpAddressNormalizer.Normalize(ip);
        _ = Task.Run(() => LogAccessCore(normalizedIp, url, method, blocked, reason));
    }

    private void LogAccessCore(string normalizedIp, string url, string method, bool blocked, string reason)
    {
        try
        {
            var student = ResolveStudent(normalizedIp);
            _db.AddLog(new AccessLog
            {
                StudentId = student?.Id ?? "",
                StudentName = student?.Name ?? normalizedIp,
                Url = url,
                Domain = _filterService.ExtractDomain(url),
                Method = method,
                WasBlocked = blocked,
                BlockReason = reason,
                StatusCode = blocked ? 403 : 200
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao gravar log de acesso do proxy");
        }
    }

    private static (string Host, int Port) ExtractConnectTarget(TunnelConnectSessionEventArgs e)
    {
        var connectRequest = e.HttpClient.ConnectRequest;
        var host = connectRequest?.Host ?? string.Empty;
        var port = connectRequest?.RequestUri?.Port;

        if (string.IsNullOrWhiteSpace(host))
        {
            host = connectRequest?.Url ?? string.Empty;
        }

        host = host.Trim();
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(host);
            host = uri.Host;
            port = uri.Port;
        }
        else if (host.StartsWith("[", StringComparison.Ordinal) && host.Contains(']'))
        {
            var endBracket = host.IndexOf(']');
            if (endBracket > 0 && host.Length > endBracket + 2 && int.TryParse(host[(endBracket + 2)..], out var parsedPort))
            {
                port = parsedPort;
            }

            host = host[1..endBracket];
        }
        else
        {
            var separator = host.LastIndexOf(':');
            if (separator > 0 && int.TryParse(host[(separator + 1)..], out var parsedPort))
            {
                port = parsedPort;
                host = host[..separator];
            }
        }

        host = _filterHostNormalize(host);
        return (host, port.GetValueOrDefault(443));

        static string _filterHostNormalize(string value)
        {
            value = value.Trim().TrimEnd('.').ToLowerInvariant();
            return value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? value[4..] : value;
        }
    }

    private void CountAllowedTunnelRequest(string clientIp, string url, bool willBeInspected)
    {
        if (!willBeInspected)
        {
            _studentManager.UpdateActivity(clientIp, url, false, 0);
        }
    }

    private static void DenyConnect(TunnelConnectSessionEventArgs e, string reason)
    {
        e.DecryptSsl = false;
        e.DenyConnect = true;
        e.HttpClient.Response.StatusCode = 403;
        e.HttpClient.Response.StatusDescription = $"ProxyEdu: acesso HTTPS bloqueado ({reason})";
        e.HttpClient.Response.ContentType = "text/plain; charset=utf-8";
    }

    private void LogProxyDecision(
        string clientIp,
        string host,
        int port,
        string type,
        string policy,
        string? matchedRule,
        bool allowed,
        string reason)
    {
        var normalizedIp = IpAddressNormalizer.Normalize(clientIp);
        var student = ResolveStudent(normalizedIp);

        _logger.LogInformation(
            "Proxy decision: host={Host} port={Port} type={Type} policy={Policy} rule={Rule} result={Result} reason={Reason} student={Student} ip={Ip}",
            host,
            port,
            type,
            policy,
            matchedRule ?? "-",
            allowed ? "allowed" : "blocked",
            reason,
            student?.Name ?? "-",
            normalizedIp);
    }

    private void NotifyHttpsBlocked(
        string clientIp,
        string host,
        int port,
        string reason,
        string policy,
        string? matchedRule)
    {
        var normalizedIp = IpAddressNormalizer.Normalize(clientIp);
        var student = ResolveStudent(normalizedIp);
        var destination = BuildBlockedPageDestination(host, reason, policy, "https");

        _ = _hub.Clients.All.SendAsync("HttpsBlocked", new
        {
            ip = normalizedIp,
            host,
            port,
            reason,
            policy,
            matchedRule,
            studentId = student?.Id ?? "",
            studentName = student?.Name ?? "",
            blockedPagePath = destination.IsLocal ? destination.Url : "",
            blockedPageUrl = destination.Url,
            blockDestination = destination.Kind,
            timestamp = DateTime.UtcNow
        });

        _logger.LogInformation(
            "HTTPS CONNECT blocked notification sent: host={Host} port={Port} destination={Destination} url={Url} student={Student} ip={Ip}",
            host,
            port,
            destination.Kind,
            destination.Url,
            student?.Name ?? "-",
            normalizedIp);
    }

    private BlockPageDestination BuildBlockedPageDestination(
        string host,
        string reason,
        string policy,
        string requestType)
    {
        var settings = _db.GetSettings();
        var query = new Dictionary<string, string>
        {
            ["host"] = host,
            ["reason"] = reason,
            ["type"] = requestType,
            ["policy"] = policy
        };

        if (!string.IsNullOrWhiteSpace(settings.BlockedRedirectUrl))
        {
            if (Uri.TryCreate(settings.BlockedRedirectUrl.Trim(), UriKind.Absolute, out var configuredUrl) &&
                (configuredUrl.Scheme == Uri.UriSchemeHttp || configuredUrl.Scheme == Uri.UriSchemeHttps))
            {
                return new BlockPageDestination(
                    AppendQuery(configuredUrl.ToString(), query),
                    "institutional redirect",
                    false);
            }

            _logger.LogWarning(
                "Configured block page URL is invalid and local /blocked fallback will be used: {BlockedRedirectUrl}",
                settings.BlockedRedirectUrl);
        }

        return new BlockPageDestination(
            AppendQuery("/blocked", query),
            "local blocked page",
            true);
    }

    private static string AppendQuery(string baseUrl, IReadOnlyDictionary<string, string> values)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var query = string.Join("&", values.Select(item =>
            $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value ?? string.Empty)}"));

        return baseUrl + separator + query;
    }

    private sealed record BlockPageDestination(string Url, string Kind, bool IsLocal);

    public override void Dispose()
    {
        _proxyServer?.Dispose();
        base.Dispose();
    }

    private void EnsureRootCertificateTrusted()
    {
        try
        {
            var certManager = _proxyServer.CertificateManager;
            certManager.EnsureRootCertificate();

            if (!certManager.IsRootCertificateMachineTrusted())
            {
                var trustedAsAdmin = certManager.TrustRootCertificateAsAdmin();
                if (!trustedAsAdmin)
                {
                    certManager.TrustRootCertificate(false);
                }
            }

            _logger.LogInformation("Certificado raiz do proxy garantido/confiavel.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao preparar certificado raiz do proxy.");
        }
    }

    private void ConfigureCertificateManager()
    {
        Directory.CreateDirectory(CertDirectory);

        var certManager = _proxyServer.CertificateManager;
        certManager.RootCertificateName = RootCertificateName;
        certManager.RootCertificateIssuerName = RootCertificateIssuer;
        certManager.PfxFilePath = RootPfxPath;
        certManager.PfxPassword = RootPfxPassword;
        certManager.OverwritePfxFile = false;
        certManager.SaveFakeCertificates = true;
        certManager.StorageFlag = X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet;
    }
}
