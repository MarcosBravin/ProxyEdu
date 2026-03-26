using Newtonsoft.Json;

namespace ProxyEdu.Client.Services;

public class HeartbeatService : BackgroundService
{
    private readonly ServerEndpointResolver _endpointResolver;

    public HeartbeatService(ServerEndpointResolver endpointResolver)
    {
        _endpointResolver = endpointResolver;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var endpoint = await _endpointResolver.ResolveAsync(stoppingToken);
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var payload = JsonConvert.SerializeObject(new
                {
                    ip = GetLocalIp(),
                    currentUrl = GetActiveWindow(),
                    timestamp = DateTime.UtcNow
                });
                await client.PostAsync(
                    $"http://{endpoint.Ip}:{endpoint.DashboardPort}/api/students/heartbeat",
                    new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
            }
            catch
            {
                _endpointResolver.Invalidate();
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
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
