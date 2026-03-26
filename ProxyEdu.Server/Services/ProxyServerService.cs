using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;
using ProxyEdu.Server.Services;
using ProxyEdu.Shared.Models;
using System.IO;
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
    private readonly ILogger<ProxyServerService> _logger;

    public ProxyServerService(
        StudentManagerService studentManager,
        FilterService filterService,
        DatabaseService db,
        ILogger<ProxyServerService> logger)
    {
        _studentManager = studentManager;
        _filterService = filterService;
        _db = db;
        _logger = logger;
        _proxyServer = new ProxyServer();
        ConfigureCertificateManager();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = _db.GetSettings();

        EnsureRootCertificateTrusted();

        _proxyServer.BeforeRequest += OnBeforeRequest;
        _proxyServer.BeforeResponse += OnBeforeResponse;
        _proxyServer.ServerCertificateValidationCallback += OnCertValidation;

        var explicitEndPoint = new ExplicitProxyEndPoint(
            System.Net.IPAddress.Any, settings.ProxyPort, decryptSsl: true);

        _proxyServer.AddEndPoint(explicitEndPoint);
        _proxyServer.Start();

        _logger.LogInformation("Proxy started on port {Port}", settings.ProxyPort);

        await Task.Delay(Timeout.Infinite, stoppingToken);

        _proxyServer.Stop();
    }

    public X509Certificate2? GetRootCertificate()
    {
        return _proxyServer.CertificateManager.RootCertificate;
    }

    private async Task OnBeforeRequest(object sender, SessionEventArgs e)
    {
        var clientIp = e.ClientRemoteEndPoint.Address.ToString();
        var url = e.HttpClient.Request.Url;
        var method = e.HttpClient.Request.Method;
        var isConnectTunnel = string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase);

        // For HTTPS, browsers first send CONNECT to establish the tunnel.
        // If we block at CONNECT, many browsers won't render a readable block page.
        // Let CONNECT pass and enforce on the next decrypted HTTP request.
        if (isConnectTunnel)
        {
            return;
        }

        _studentManager.TouchHeartbeat(clientIp, url);

        // Check if student is completely blocked
        if (_studentManager.IsStudentBlocked(clientIp))
        {
            await ServeBlockPage(e, "Sua internet foi bloqueada pelo professor.");
            _studentManager.UpdateActivity(clientIp, url, true, 0);
            LogAccess(clientIp, url, method, true, "Aluno bloqueado");
            return;
        }

        // Explicit per-student bypass: ignore blacklist/whitelist filters
        if (_studentManager.IsStudentBypassFilters(clientIp))
        {
            _studentManager.UpdateActivity(clientIp, url, false, 0);
            LogAccess(clientIp, url, method, false, "Liberacao total do aluno");
            return;
        }

        // Check URL filter
        if (!_filterService.IsUrlAllowed(url, clientIp))
        {
            await ServeBlockPage(e, $"Acesso ao site {_filterService.ExtractDomain(url)} foi bloqueado.");
            _studentManager.UpdateActivity(clientIp, url, true, 0);
            LogAccess(clientIp, url, method, true, "URL bloqueada por filtro");
            return;
        }

        _studentManager.UpdateActivity(clientIp, url, false, 0);
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

    private async Task ServeBlockPage(SessionEventArgs e, string message)
    {
        var settings = _db.GetSettings();
        var safeMessage = System.Net.WebUtility.HtmlEncode(message);
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

    <div class='domain'>{safeMessage}</div>
    <div class='brand'>ProxyEdu - Ambiente Educacional</div>
  </div>
</body>
</html>";

        e.Ok(html);
    }

    private void LogAccess(string ip, string url, string method, bool blocked, string reason = "")
    {
        var student = _db.Students.FindOne(s => s.IpAddress == ip);
        _db.AddLog(new AccessLog
        {
            StudentId = student?.Id ?? "",
            StudentName = student?.Name ?? ip,
            Url = url,
            Domain = _filterService.ExtractDomain(url),
            Method = method,
            WasBlocked = blocked,
            BlockReason = reason,
            StatusCode = blocked ? 403 : 200
        });
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
