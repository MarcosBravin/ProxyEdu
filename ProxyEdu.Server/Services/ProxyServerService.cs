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
            await ServeBlockPage(
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
            await ServeBlockPage(e, decision.Domain, decision.Reason, decision.Policy, requestType, clientIp);
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

    private async Task ServeBlockPage(
        SessionEventArgs e,
        string domain,
        string reason,
        string policy,
        string blockType,
        string clientIp)
    {
        var student = ResolveStudent(clientIp);
        var safeDomain = WebUtility.HtmlEncode(domain);
        var safeReason = WebUtility.HtmlEncode(reason);
        var safePolicy = WebUtility.HtmlEncode(policy);
        var safeBlockType = WebUtility.HtmlEncode(blockType);
        var safeStudent = WebUtility.HtmlEncode(student?.Name ?? clientIp);
        var safeTimestamp = WebUtility.HtmlEncode(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
        var html = $@"<!DOCTYPE html>
<html lang='pt-BR'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>Foco na Aula — ProxyEdu</title>
<style>
  * {{ margin:0; padding:0; box-sizing:border-box; }}
  body {{
    background: radial-gradient(1000px 620px at 20% -10%, #e8f1ff 0%, #f6f9ff 55%, #f8fbff 100%);
    color:#12325f;
    font-family:'Manrope','Segoe UI',sans-serif;
    display:flex;
    align-items:center;
    justify-content:center;
    min-height:100vh;
    padding:20px;
  }}
  .card {{
    width:100%;
    max-width:700px;
    background: #ffffff;
    border:1px solid #d5e2f4;
    border-radius:20px;
    padding:38px 34px;
    box-shadow: 0 14px 34px rgba(20, 57, 110, 0.12);
  }}
  .top {{
    display:flex;
    align-items:center;
    gap:14px;
    margin-bottom:18px;
  }}
  .icon {{
    width:44px;
    height:44px;
    border-radius:12px;
    display:flex;
    align-items:center;
    justify-content:center;
    background: linear-gradient(135deg,#1d4ed8,#0ea5e9);
    color:#fff;
    font-size:22px;
    font-weight:800;
  }}
  h1 {{
    font-size:28px;
    line-height:1.15;
    letter-spacing:-0.02em;
    color:#173b6f;
  }}
  .subtitle {{
    color:#40618f;
    font-size:15px;
    margin-bottom:16px;
  }}
  .tips {{
    background:#f8fbff;
    border:1px solid #d8e6f8;
    border-radius:14px;
    padding:14px 16px;
    margin-bottom:16px;
  }}
  .scenario {{
    background:#fff8e8;
    border:1px solid #f3dcaa;
    border-radius:14px;
    padding:13px 15px;
    margin-bottom:16px;
  }}
  .scenario-text {{
    color:#6f4f00;
    font-size:14px;
    line-height:1.6;
  }}
  .quote {{
    background:#eef6ff;
    border:1px solid #cfe2fb;
    border-left:4px solid #3b82f6;
    border-radius:12px;
    padding:12px 14px;
    margin-bottom:16px;
  }}
  .quote-text {{
    color:#1f3f71;
    font-size:14px;
    line-height:1.6;
    font-weight:600;
  }}
  .quote-author {{
    color:#4c6fa3;
    font-size:12px;
    margin-top:6px;
  }}
  .tips h2 {{
    font-size:14px;
    color:#2563eb;
    margin-bottom:8px;
    text-transform:uppercase;
    letter-spacing:.08em;
  }}
  .tips ul {{
    margin-left:18px;
    color:#1f3861;
    line-height:1.65;
    font-size:14px;
  }}
  .domain {{
    color:#2c4f83;
    font-family:ui-monospace,Consolas,'DM Mono',monospace;
    font-size:12px;
    word-break:break-word;
    background:#f3f8ff;
    border:1px solid #cadbf5;
    border-radius:10px;
    padding:9px 12px;
  }}
  .brand {{
    margin-top:16px;
    color:#5e7ba8;
    font-size:12px;
    text-align:center;
    letter-spacing:.04em;
  }}
  @media (prefers-color-scheme: dark) {{
    body {{
      background: radial-gradient(1200px 700px at 20% -10%, #172445 0%, #070d1b 55%, #050a14 100%);
      color:#dbe7ff;
    }}
    .card {{
      background: rgba(9,16,32,0.88);
      border:1px solid #22314d;
      box-shadow: 0 20px 50px rgba(0,0,0,0.42);
    }}
    h1 {{ color:#e2edff; }}
    .subtitle {{ color:#9bb0d4; }}
    .tips {{
      background:rgba(12,23,44,0.7);
      border:1px solid #1e325c;
    }}
    .tips h2 {{ color:#93c5fd; }}
    .tips ul {{ color:#d3def3; }}
    .domain {{
      color:#9fb2d4;
      background:#0b1429;
      border:1px solid #1f3158;
    }}
    .brand {{ color:#6b87b6; }}
    .scenario {{
      background:rgba(56,42,10,0.55);
      border:1px solid #6b5322;
    }}
    .scenario-text {{ color:#fde68a; }}
    .quote {{
      background:rgba(14, 36, 69, 0.75);
      border:1px solid #2a4f85;
      border-left:4px solid #60a5fa;
    }}
    .quote-text {{ color:#dbeafe; }}
    .quote-author {{ color:#9ec5ff; }}
  }}
</style>
</head>
<body>
  <div class='card'>
    <div class='top'>
      <div class='icon'>F</div>
      <h1>Foco na Aula</h1>
    </div>
    <div class='subtitle'>Este conte&uacute;do est&aacute; restrito neste momento para ajudar na concentra&ccedil;&atilde;o da turma.</div>

    <div class='tips'>
      <h2>O que fazer agora</h2>
      <ul>
        <li>Volte para a atividade indicada na aula.</li>
        <li>Use os materiais oficiais da disciplina.</li>
        <li>Se este site for necess&aacute;rio para a tarefa, avise o professor.</li>
      </ul>
    </div>

    <div class='scenario'>
      <div class='scenario-text'>
        Neste momento, a turma est&aacute; em atividade guiada. O objetivo &eacute; manter aten&ccedil;&atilde;o no conte&uacute;do da aula e concluir a tarefa dentro do tempo planejado.
      </div>
    </div>

    <div class='quote'>
      <div class='quote-text'>&ldquo;A educa&ccedil;&atilde;o &eacute; a arma mais poderosa que voc&ecirc; pode usar para mudar o mundo.&rdquo;</div>
      <div class='quote-author'>Nelson Mandela</div>
    </div>

    <div class='domain'>
      <div><strong>Dominio:</strong> {safeDomain}</div>
      <div><strong>Motivo:</strong> {safeReason}</div>
      <div><strong>Politica:</strong> {safePolicy}</div>
      <div><strong>Tipo:</strong> {safeBlockType}</div>
      <div><strong>Dispositivo/aluno:</strong> {safeStudent}</div>
      <div><strong>Horario:</strong> {safeTimestamp}</div>
    </div>
    <div class='brand'>ProxyEdu - Ambiente Educacional</div>
  </div>
</body>
</html>";

        e.Ok(html);
        e.HttpClient.Response.StatusCode = 403;
        e.HttpClient.Response.StatusDescription = "ProxyEdu: acesso bloqueado";
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
        var blockedPagePath = BuildBlockedPagePath(host, reason, policy, student?.Name);

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
            blockedPagePath,
            timestamp = DateTime.UtcNow
        });
    }

    private static string BuildBlockedPagePath(string host, string reason, string policy, string? studentName)
    {
        var query = new[]
        {
            $"host={Uri.EscapeDataString(host)}",
            $"reason={Uri.EscapeDataString(reason)}",
            $"policy={Uri.EscapeDataString(policy)}",
            "type=https",
            $"student={Uri.EscapeDataString(studentName ?? string.Empty)}"
        };

        return "/blocked?" + string.Join("&", query);
    }

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
