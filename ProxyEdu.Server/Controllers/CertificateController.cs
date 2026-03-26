using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Services;
using System.Security.Cryptography.X509Certificates;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/certificate")]
public class CertificateController : ControllerBase
{
    private readonly ProxyServerService _proxyServerService;

    public CertificateController(ProxyServerService proxyServerService)
    {
        _proxyServerService = proxyServerService;
    }

    [HttpGet("root")]
    public IActionResult GetRootCertificate()
    {
        var cert = _proxyServerService.GetRootCertificate();
        if (cert is null)
        {
            return NotFound(new { message = "Certificado raiz ainda nao foi inicializado." });
        }

        var certBytes = cert.Export(X509ContentType.Cert);
        return File(certBytes, "application/x-x509-ca-cert", "proxyedu-root.cer");
    }
}
