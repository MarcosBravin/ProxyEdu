using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace ProxyEdu.Server.Controllers;

[ApiController]
public class BlockedController : ControllerBase
{
    [HttpGet("blocked")]
    public IActionResult Index(
        [FromQuery] string? host,
        [FromQuery] string? reason,
        [FromQuery] string? policy,
        [FromQuery] string? type,
        [FromQuery] string? student)
    {
        var safeHost = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(host) ? "site solicitado" : host);
        var safeReason = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(reason) ? "bloqueado por politica da escola" : reason);
        var safePolicy = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(policy) ? "politica escolar" : policy);
        var safeType = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(type) ? "https" : type);
        var safeStudent = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(student) ? "dispositivo nao identificado" : student);
        var timestamp = WebUtility.HtmlEncode(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));

        var html = $@"<!DOCTYPE html>
<html lang='pt-BR'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>Site bloqueado - ProxyEdu</title>
<style>
body {{ margin:0; min-height:100vh; display:flex; align-items:center; justify-content:center; background:#f4f7fb; color:#0f172a; font-family:Segoe UI,Arial,sans-serif; padding:24px; }}
.card {{ width:100%; max-width:720px; background:white; border:1px solid #d4deea; border-radius:14px; padding:32px; box-shadow:0 14px 34px rgba(20,57,110,.12); }}
.icon {{ width:48px; height:48px; border-radius:12px; background:#2563eb; color:white; display:flex; align-items:center; justify-content:center; font-weight:800; font-size:24px; margin-bottom:16px; }}
h1 {{ margin:0 0 8px; font-size:28px; color:#173b6f; }}
p {{ color:#334155; line-height:1.55; }}
.grid {{ margin-top:20px; display:grid; gap:10px; }}
.row {{ background:#f8fbff; border:1px solid #d8e6f8; border-radius:10px; padding:10px 12px; }}
.label {{ display:block; color:#64748b; font-size:12px; text-transform:uppercase; font-weight:700; letter-spacing:.06em; margin-bottom:3px; }}
.value {{ color:#12325f; word-break:break-word; }}
</style>
</head>
<body>
<main class='card'>
  <div class='icon'>!</div>
  <h1>Site bloqueado</h1>
  <p>Este site HTTPS foi bloqueado por uma politica da escola. Como a inspecao HTTPS esta desativada, o ProxyEdu bloqueou o tunel seguro sem alterar certificados do navegador.</p>
  <div class='grid'>
    <div class='row'><span class='label'>Dominio</span><span class='value'>{safeHost}</span></div>
    <div class='row'><span class='label'>Motivo</span><span class='value'>{safeReason}</span></div>
    <div class='row'><span class='label'>Politica</span><span class='value'>{safePolicy}</span></div>
    <div class='row'><span class='label'>Tipo</span><span class='value'>{safeType}</span></div>
    <div class='row'><span class='label'>Dispositivo/aluno</span><span class='value'>{safeStudent}</span></div>
    <div class='row'><span class='label'>Horario</span><span class='value'>{timestamp}</span></div>
  </div>
</main>
</body>
</html>";

        return Content(html, "text/html; charset=utf-8");
    }
}
