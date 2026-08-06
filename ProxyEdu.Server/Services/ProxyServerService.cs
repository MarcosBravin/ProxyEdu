using System.Collections.Concurrent;
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
    private const string RootCertificateName = "ProxyEdu Root CA";
    private const string RootCertificateIssuer = "ProxyEdu";

    private static readonly TimeSpan StaleSessionCleanupInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(10);

    private readonly ProxyServer _proxyServer;
    private readonly StudentManagerService _studentManager;
    private readonly FilterService _filterService;
    private readonly DatabaseService _db;
    private readonly LogQueueService _logQueue;
    private readonly ILogger<ProxyServerService> _logger;
    private readonly RuntimeDiagnostics _diagnostics;
    private readonly string _rootPfxPassword;

    private readonly ConcurrentDictionary<string, DateTime> _sessionLastActivity = new(StringComparer.OrdinalIgnoreCase);

    public ProxyServerService(
        StudentManagerService studentManager,
        FilterService filterService,
        DatabaseService db,
        LogQueueService logQueue,
        ILogger<ProxyServerService> logger,
        IConfiguration configuration,
        RuntimeDiagnostics diagnostics)
    {
        _studentManager = studentManager;
        _filterService = filterService;
        _db = db;
        _logQueue = logQueue;
        _logger = logger;
        _diagnostics = diagnostics;
        _proxyServer = new ProxyServer();
        _proxyServer.ExceptionFunc = OnProxyException;

        _rootPfxPassword = CertificatePasswordStore.Resolve(CertDirectory, configuration);

        ConfigureCertificateManager();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = _db.GetSettings();
        EnsureRootCertificateTrusted();

        _proxyServer.ConnectTimeOutSeconds = 15;
        _proxyServer.ConnectionTimeOutSeconds = 60;

        _proxyServer.BeforeRequest += OnBeforeRequest;
        _proxyServer.BeforeResponse += OnBeforeResponse;

        var explicitEndPoint = new ExplicitProxyEndPoint(
            System.Net.IPAddress.Any, settings.ProxyPort, decryptSsl: true);

        _proxyServer.AddEndPoint(explicitEndPoint);
        _proxyServer.Start();
        _logger.LogInformation("Proxy started on port {Port}", settings.ProxyPort);

        try
        {
            await RunStaleSessionCleanupAsync(stoppingToken);
        }
        finally
        {
            _proxyServer.Stop();
        }
    }

    private async Task RunStaleSessionCleanupAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(StaleSessionCleanupInterval, stoppingToken);
                var now = DateTime.UtcNow;
                var staleSessions = _sessionLastActivity
                    .Where(kvp => (now - kvp.Value) > SessionIdleTimeout)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var session in staleSessions)
                {
                    _sessionLastActivity.TryRemove(session, out _);
                }

                if (staleSessions.Count > 0)
                {
                    _logger.LogInformation("Limpeza de sessões stale: {Count} sessões inativas removidas", staleSessions.Count);
                    foreach (var sessionIp in staleSessions)
                    {
                        _studentManager.MarkDisconnected(sessionIp);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "Erro na limpeza de sessões stale"); }
        }
    }

    private void TouchSession(string clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp)) return;
        _sessionLastActivity[clientIp] = DateTime.UtcNow;
    }

    private bool IsSessionStale(string clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp)) return false;
        if (_sessionLastActivity.TryGetValue(clientIp, out var lastActivity))
        {
            return (DateTime.UtcNow - lastActivity) > SessionIdleTimeout;
        }
        return false;
    }

    public X509Certificate2? GetRootCertificate()
    {
        return _proxyServer.CertificateManager.RootCertificate;
    }

    public (bool IsRunning, int ClientConnections) GetRuntimeState() => (_proxyServer.ProxyRunning, _proxyServer.ClientConnectionCount);

    private void OnProxyException(Exception exception)
    {
        _diagnostics.RecordException(exception);
        _logger.LogError(exception, "TitaniumProxyException {ExceptionType}", exception.GetType().Name);
    }

    private async Task OnBeforeRequest(object sender, SessionEventArgs e)
    {
        var clientIp = e.ClientRemoteEndPoint.Address.ToString();
        var url = e.HttpClient.Request.Url;
        var method = e.HttpClient.Request.Method;
        var isConnectTunnel = string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase);
        var trace = _diagnostics.BeginRequest(e.ClientConnectionId.ToString(), e.ServerConnectionId.ToString(), e.IsHttps, isConnectTunnel);
        e.UserData = trace;
        _logger.LogDebug("ProxyRequestStarted {TraceId} {ClientConnectionId} {ServerConnectionId} {ClientIp} {Method} {IsHttps} {IsConnect}", trace.Id, trace.ClientConnectionId, trace.ServerConnectionId, clientIp, method, trace.IsHttps, isConnectTunnel);

        if (IsSessionStale(clientIp))
        {
            _logger.LogInformation("Sessão stale detectada para {Ip}, forçando renovação de conexão", clientIp);
            _studentManager.MarkDisconnected(clientIp);
            TouchSession(clientIp);
        }
        else
        {
            TouchSession(clientIp);
        }

        // CONNECT tunnel filtering: verificar o domínio ANTES de estabelecer o túnel HTTPS
        if (isConnectTunnel)
        {
            var connectHost = url ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(connectHost))
            {
                var colonIndex = connectHost.LastIndexOf(':');
                var hostname = colonIndex > 0 ? connectHost[..colonIndex] : connectHost;

                if (_studentManager.IsStudentBlocked(clientIp))
                {
                    _logger.LogInformation("CONNECT bloqueado para {Ip}: {Host} (aluno bloqueado)", clientIp, hostname);
                    e.HttpClient.Response.StatusCode = 403;
                    return;
                }

                if (_studentManager.IsStudentBypassFilters(clientIp))
                {
                    return;
                }

                var testUrl = $"https://{hostname}/";
                if (!_filterService.IsUrlAllowed(testUrl, clientIp))
                {
                    _logger.LogInformation("CONNECT bloqueado para {Ip}: {Host} (domínio na blacklist)", clientIp, hostname);
                    e.HttpClient.Response.StatusCode = 403;
                    _studentManager.UpdateActivity(clientIp, $"CONNECT:{hostname}", true, 0);
                    LogAccess(clientIp, testUrl, "CONNECT", true, "Domínio bloqueado por filtro (CONNECT)");
                    return;
                }
            }
            return;
        }

        _studentManager.TouchHeartbeat(clientIp, url);

        if (_studentManager.IsStudentBlocked(clientIp))
        {
            await ServeBlockPage(e, "Sua internet foi bloqueada pelo professor.");
            _studentManager.UpdateActivity(clientIp, url, true, 0);
            LogAccess(clientIp, url, method, true, "Aluno bloqueado");
            return;
        }

        if (_studentManager.IsStudentBypassFilters(clientIp))
        {
            _studentManager.UpdateActivity(clientIp, url, false, 0);
            LogAccess(clientIp, url, method, false, "Liberacao total do aluno");
            return;
        }

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
        TouchSession(clientIp);

        var size = e.HttpClient.Response.ContentLength;
        _studentManager.TouchHeartbeat(clientIp, e.HttpClient.Request.Url);
        _studentManager.UpdateActivity(clientIp, e.HttpClient.Request.Url, false, Math.Max(0, size));
        var trace = e.UserData as ProxyRequestTrace;
        var completion = _diagnostics.Complete(trace, e.HttpClient.Response.StatusCode);
        if (completion.Elapsed > TimeSpan.FromSeconds(5) || completion.StatusCode >= 500)
        {
            _logger.LogWarning("ProxyRequestCompleted {TraceId} {ClientConnectionId} {ServerConnectionId} {StatusCode} {ElapsedMs} {IsTunnelSetup}", trace?.Id, trace?.ClientConnectionId, trace?.ServerConnectionId, completion.StatusCode, completion.Elapsed.TotalMilliseconds, completion.IsTunnelSetup);
        }
        else
        {
            _logger.LogDebug("ProxyRequestCompleted {TraceId} {StatusCode} {ElapsedMs}", trace?.Id, completion.StatusCode, completion.Elapsed.TotalMilliseconds);
        }
    }

    private async Task ServeBlockPage(SessionEventArgs e, string message)
    {
        var settings = _db.GetSettings();
        var safeMessage = System.Net.WebUtility.HtmlEncode(message);
        var html = $$$$$$"""
<!DOCTYPE html>
<html lang="pt-BR">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1">
  <meta name="theme-color" content="#0739b7">
  <title>Acesso restrito</title>
  <style>
    :root{--brand-blue:#0739b7;--brand-blue-dark:#03226f;--brand-blue-light:#1769e8;--brand-red:#e6242f;--brand-red-dark:#b91520;--white:#fff;--text-primary:#10264f;--text-secondary:#546583;--border-color:#d4e0f6;--soft-blue:#edf4ff;--soft-red:#fff1f2}
    *{margin:0;padding:0;box-sizing:border-box}html{min-height:100%}
    body{min-height:100vh;background:radial-gradient(circle at 12% 10%,rgba(55,126,255,.24) 0,transparent 32%),radial-gradient(circle at 90% 88%,rgba(230,36,47,.14) 0,transparent 28%),linear-gradient(145deg,#e8f0ff 0%,#f8faff 47%,#edf3ff 100%);color:var(--text-primary);font-family:"Segoe UI",Arial,Helvetica,sans-serif;display:flex;align-items:center;justify-content:center;padding:28px 18px;overflow-x:hidden}
    body::before{content:"";position:fixed;top:-180px;right:-180px;width:430px;height:430px;border-radius:50%;border:72px solid rgba(7,57,183,.05);pointer-events:none}
    body::after{content:"";position:fixed;left:-150px;bottom:-170px;width:360px;height:360px;border-radius:50%;border:60px solid rgba(230,36,47,.045);pointer-events:none}
    .page{width:100%;max-width:920px;position:relative;z-index:1}.card{overflow:hidden;background:rgba(255,255,255,.98);border:1px solid rgba(190,208,240,.9);border-radius:30px;box-shadow:0 30px 75px rgba(9,43,111,.18),0 4px 14px rgba(9,43,111,.08)}
    .brand-header{min-height:132px;padding:28px 36px;display:flex;align-items:center;justify-content:space-between;gap:24px;position:relative;overflow:hidden;color:var(--white);background:radial-gradient(circle at 82% 20%,rgba(255,255,255,.14) 0,transparent 25%),linear-gradient(115deg,var(--brand-blue-dark) 0%,var(--brand-blue) 58%,var(--brand-blue-light) 100%)}
    .brand-header::before{content:"";position:absolute;left:122px;bottom:-92px;width:220px;height:150px;border-radius:50%;background:rgba(255,255,255,.045);transform:rotate(-12deg)}
    .brand-header::after{content:"";position:absolute;top:-65px;right:-35px;width:230px;height:230px;border-radius:50%;border:38px solid rgba(255,255,255,.08)}
    .header-main{display:flex;align-items:center;gap:18px;position:relative;z-index:2}.header-icon{width:64px;height:64px;flex:0 0 64px;display:flex;align-items:center;justify-content:center;color:var(--white);background:rgba(255,255,255,.14);border:1px solid rgba(255,255,255,.25);border-radius:20px;box-shadow:0 12px 30px rgba(0,20,75,.24);backdrop-filter:blur(8px)}.header-icon svg{width:34px;height:34px}
    .header-eyebrow{margin-bottom:7px;color:rgba(255,255,255,.72);font-size:10px;font-weight:800;letter-spacing:.18em;text-transform:uppercase}.header-title{color:var(--white);font-size:28px;line-height:1.05;font-weight:800;letter-spacing:-.035em}
    .header-badge{position:relative;z-index:2;display:inline-flex;align-items:center;gap:9px;padding:9px 13px;color:rgba(255,255,255,.9);background:rgba(3,25,84,.24);border:1px solid rgba(255,255,255,.18);border-radius:999px;font-size:11px;font-weight:700;letter-spacing:.04em}.header-badge::before{content:"";width:8px;height:8px;border-radius:50%;background:#7df0b0;box-shadow:0 0 0 4px rgba(125,240,176,.14)}
    .red-line{width:100%;height:7px;background:linear-gradient(90deg,var(--brand-red-dark),var(--brand-red),#ff4d58)}.content{padding:40px 42px 34px}
    .status{display:inline-flex;align-items:center;gap:9px;margin-bottom:20px;padding:8px 13px;color:var(--brand-red-dark);background:var(--soft-red);border:1px solid #ffc9ce;border-radius:999px;font-size:12px;font-weight:800;letter-spacing:.09em;text-transform:uppercase}.status-dot{width:9px;height:9px;background:var(--brand-red);border-radius:50%;box-shadow:0 0 0 5px rgba(230,36,47,.12)}
    .hero{display:grid;grid-template-columns:minmax(0,1fr) 190px;align-items:center;gap:36px;padding-bottom:30px;border-bottom:1px solid var(--border-color)}h1{max-width:610px;color:var(--brand-blue-dark);font-size:clamp(32px,5vw,50px);line-height:1.06;font-weight:850;letter-spacing:-.045em}h1 strong{color:var(--brand-red);font-weight:850}.subtitle{max-width:600px;margin-top:17px;color:var(--text-secondary);font-size:17px;line-height:1.7}.subtitle strong{color:var(--brand-blue)}
    .focus-illustration{width:180px;height:180px;display:flex;align-items:center;justify-content:center;position:relative;background:linear-gradient(145deg,#f5f8ff,#e6efff);border:1px solid #cbdaf5;border-radius:50%}.focus-illustration::before{content:"";position:absolute;width:135px;height:135px;border:2px dashed rgba(7,57,183,.25);border-radius:50%}.shield{width:86px;height:96px;display:flex;align-items:center;justify-content:center;position:relative;z-index:2;background:linear-gradient(145deg,var(--brand-blue-light),var(--brand-blue-dark));border-radius:42px 42px 48px 48px;box-shadow:0 18px 30px rgba(7,57,183,.28);clip-path:polygon(50% 0%,93% 17%,88% 67%,50% 100%,12% 67%,7% 17%)}.lock-icon{width:44px;height:48px;display:block;transform:translateY(-1px)}
    .message-grid{display:grid;grid-template-columns:1fr 1fr;gap:17px;margin-top:26px}.info-box{min-height:158px;padding:21px;background:var(--soft-blue);border:1px solid #ceddf7;border-radius:18px}.info-box.primary{background:linear-gradient(145deg,#edf4ff,#f8fbff);border-top:4px solid var(--brand-blue)}.info-box.warning{background:linear-gradient(145deg,#fff5f5,#fffafa);border-color:#ffd3d7;border-top:4px solid var(--brand-red)}
    .info-title{display:flex;align-items:center;gap:10px;margin-bottom:12px;color:var(--brand-blue-dark);font-size:15px;font-weight:800}.warning .info-title{color:var(--brand-red-dark)}.number{width:29px;height:29px;display:inline-flex;align-items:center;justify-content:center;color:var(--white);background:var(--brand-blue);border-radius:9px;font-size:13px;font-weight:800}.warning .number{background:var(--brand-red)}.info-text{color:#4e607f;font-size:14px;line-height:1.65}.info-text strong{color:var(--brand-blue-dark)}.warning .info-text strong{color:var(--brand-red-dark)}
    .blocked-resource{margin-top:22px;padding:17px 19px;display:flex;align-items:center;gap:14px;background:#f8faff;border:1px solid var(--border-color);border-radius:16px}.resource-icon{width:42px;height:42px;flex:0 0 42px;display:flex;align-items:center;justify-content:center;color:var(--brand-blue);background:#e6efff;border-radius:12px;font-size:21px;font-weight:900}.resource-content{min-width:0}.resource-label{color:#72809a;font-size:11px;font-weight:800;letter-spacing:.09em;text-transform:uppercase}.domain{margin-top:5px;color:#244370;font-family:Consolas,"Courier New",monospace;font-size:13px;line-height:1.45;word-break:break-all}
    .footer{padding:0 42px 28px;display:flex;align-items:center;justify-content:space-between;gap:20px;color:#71809b;font-size:11px}.system-brand{font-weight:600;text-align:right}
    @media(max-width:720px){body{padding:15px 12px;align-items:flex-start}.card{border-radius:22px}.brand-header{min-height:auto;padding:21px 20px}.header-main{gap:14px}.header-icon{width:54px;height:54px;flex-basis:54px;border-radius:16px}.header-icon svg{width:29px;height:29px}.header-title{font-size:23px}.header-badge{display:none}.content{padding:30px 21px 25px}.hero{grid-template-columns:1fr;gap:22px}.focus-illustration{width:130px;height:130px;grid-row:1}.focus-illustration::before{width:100px;height:100px}.shield{width:65px;height:74px}.lock-icon{width:35px;height:39px}h1{font-size:35px}.subtitle{font-size:15px}.message-grid{grid-template-columns:1fr}.info-box{min-height:auto}.footer{padding:0 21px 24px;flex-direction:column;align-items:flex-start;gap:6px}.system-brand{text-align:left}}
    @media(max-width:390px){.header-title{font-size:20px}.header-eyebrow{font-size:8px}h1{font-size:31px}.content{padding-left:17px;padding-right:17px}}
    @media(prefers-reduced-motion:no-preference){.card{animation:card-entry .55s ease-out both}.status-dot{animation:status-pulse 2.2s ease-in-out infinite}@keyframes card-entry{from{opacity:0;transform:translateY(18px) scale(.985)}to{opacity:1;transform:translateY(0) scale(1)}}@keyframes status-pulse{0%,100%{box-shadow:0 0 0 4px rgba(230,36,47,.1)}50%{box-shadow:0 0 0 8px rgba(230,36,47,.03)}}}
  </style>
</head>
<body>
  <main class="page">
    <section class="card">
      <header class="brand-header">
        <div class="header-main">
          <div class="header-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none"><path d="M12 3 19 6v5c0 4.6-2.9 8.1-7 10-4.1-1.9-7-5.4-7-10V6l7-3Z" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/><path d="m9 12 2 2 4-4" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
          </div>
          <div><div class="header-eyebrow">Ambiente de aprendizagem</div><div class="header-title">Navegação segura</div></div>
        </div>
        <div class="header-badge">Sessão protegida</div>
      </header>
      <div class="red-line"></div>
      <div class="content">
        <div class="status"><span class="status-dot"></span>Acesso temporariamente restrito</div>
        <div class="hero">
          <div><h1>Este conteúdo não faz parte da <strong>atividade atual.</strong></h1><p class="subtitle">O acesso foi interrompido para ajudar você a manter o <strong>foco na aula</strong> e aproveitar melhor o seu momento de aprendizagem.</p></div>
          <div class="focus-illustration" aria-hidden="true"><div class="shield"><svg class="lock-icon" viewBox="0 0 48 52" fill="none"><path d="M13 23v-7C13 9.9 17.9 5 24 5s11 4.9 11 11v7" stroke="white" stroke-width="7" stroke-linecap="round"/><rect x="7" y="20" width="34" height="28" rx="7" fill="white"/><circle cx="24" cy="32" r="3.5" fill="#0739b7"/><rect x="21.5" y="32" width="5" height="9" rx="2.5" fill="#0739b7"/></svg></div></div>
        </div>
        <div class="message-grid">
          <section class="info-box primary"><div class="info-title"><span class="number">1</span>Continue sua atividade</div><p class="info-text">Retorne ao sistema, material ou exercício indicado pelo professor e continue acompanhando a explicação da aula.</p></section>
          <section class="info-box warning"><div class="info-title"><span class="number">2</span>Precisa acessar este site?</div><p class="info-text">Caso o conteúdo seja necessário para realizar a atividade, <strong>solicite a liberação ao professor.</strong></p></section>
        </div>
        <div class="blocked-resource"><div class="resource-icon" aria-hidden="true">!</div><div class="resource-content"><div class="resource-label">Informação do bloqueio</div><div class="domain">{{{{{{safeMessage}}}}}}</div></div></div>
      </div>
      <footer class="footer"><div class="system-brand">Proteção educacional fornecida pelo ProxyEdu</div></footer>
    </section>
  </main>
</body>
</html>
""";
#if false
        var oldHtml = $@"<!DOCTYPE html>
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

    <div class='quote'>
      <div class='quote-text'>&ldquo;A educa&ccedil;&atilde;o &eacute; a arma mais poderosa que voc&ecirc; pode usar para mudar o mundo.&rdquo;</div>
      <div class='quote-author'>Nelson Mandela</div>

    <div class='domain'>{safeMessage}</div>
    <div class='brand'>ProxyEdu - Ambiente Educacional</div>
</body>
</html>";
#endif

        e.Ok(html);
    }

    private void LogAccess(string ip, string url, string method, bool blocked, string reason = "")
    {
        var domain = _filterService.ExtractDomain(url);
        var student = _db.Students.FindOne(s => s.IpAddress == ip);

        _logQueue.Enqueue(new AccessLog
        {
            StudentId = student?.Id ?? "",
            StudentName = student?.Name ?? ip,
            Url = url,
            Domain = domain,
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

            var root = certManager.RootCertificate;
            if (root is null || root.NotBefore.ToUniversalTime() > DateTime.UtcNow || root.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
            {
                throw new InvalidOperationException("A CA raiz do proxy não está disponível ou está fora do período de validade.");
            }

            _logger.LogInformation("Certificado raiz do proxy garantido/confiável.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao preparar certificado raiz do proxy.");
            throw new InvalidOperationException("O proxy não pode iniciar sem uma CA raiz válida.", ex);
        }
    }

    private void ConfigureCertificateManager()
    {
        Directory.CreateDirectory(CertDirectory);

        var certManager = _proxyServer.CertificateManager;
        certManager.RootCertificateName = RootCertificateName;
        certManager.RootCertificateIssuerName = RootCertificateIssuer;
        certManager.PfxFilePath = RootPfxPath;
        certManager.PfxPassword = _rootPfxPassword;
        certManager.OverwritePfxFile = false;
        certManager.SaveFakeCertificates = false;
        certManager.StorageFlag = X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet;
    }
}
