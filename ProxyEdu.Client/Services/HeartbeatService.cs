using Newtonsoft.Json;

namespace ProxyEdu.Client.Services;

public class HeartbeatService : BackgroundService
{
    private readonly ServerEndpointResolver _endpointResolver;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly ServerApiClient _serverApi;

    // Exponential backoff for retry
    private int _retryDelayMs = 2000;
    private const int MaxRetryDelayMs = 30000;
    private const int InitialRetryDelayMs = 2000;
    private const int NormalHeartbeatIntervalMs = 5000;

    public HeartbeatService(ServerEndpointResolver endpointResolver, ServerApiClient serverApi, ILogger<HeartbeatService> logger)
    {
        _endpointResolver = endpointResolver;
        _logger = logger;
        _serverApi = serverApi;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var endpoint = await _endpointResolver.ResolveAsync(stoppingToken);
                var payload = new
                {
                    ip = GetLocalIp(),
                    currentUrl = GetActiveWindow(),
                    timestamp = DateTime.UtcNow
                };
                await _serverApi.PostAsync(endpoint, "/api/students/heartbeat", payload, stoppingToken);

                // Reset backoff on success
                _retryDelayMs = InitialRetryDelayMs;
                await Task.Delay(NormalHeartbeatIntervalMs, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Heartbeat falhou: {Message}", ex.Message);
                _endpointResolver.Invalidate();

                // Exponential backoff on failure
                var delay = TimeSpan.FromMilliseconds(_retryDelayMs);
                _logger.LogDebug("Heartbeat backoff: aguardando {Delay}ms", _retryDelayMs);
                await Task.Delay(delay, stoppingToken);
                _retryDelayMs = Math.Min((int)(_retryDelayMs * 1.5), MaxRetryDelayMs);
            }
        }
    }

    private static string GetLocalIp()
    {
        try
        {
            return System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName())
                .AddressList
                .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?.ToString() ?? "unknown";
        }
        catch { return "unknown"; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    private static string GetActiveWindow()
    {
        var buff = new System.Text.StringBuilder(256);
        var hwnd = GetForegroundWindow();
        GetWindowText(hwnd, buff, 256);
        return buff.ToString();
    }
}
