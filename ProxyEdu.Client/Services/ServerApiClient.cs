using Newtonsoft.Json;
using System.Net;

namespace ProxyEdu.Client.Services;

/// <summary>Cliente HTTP único para o canal de controle. Nunca usa o proxy que ele mesmo administra.</summary>
public sealed class ServerApiClient : IDisposable
{
    private readonly HttpClient _client;

    public ServerApiClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = true
        };
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<byte[]> GetCertificateAsync(ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(Url(endpoint, "/api/certificate/root"), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<bool> IsHealthyAsync(ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(Url(endpoint, "/api/health"), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task PostAsync(ServerEndpoint endpoint, string path, object payload, CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync(Url(endpoint, path), content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static string Url(ServerEndpoint endpoint, string path) => $"http://{endpoint.Ip}:{endpoint.DashboardPort}{path}";
    public void Dispose() => _client.Dispose();
}
