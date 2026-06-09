using Microsoft.AspNetCore.SignalR;

namespace ProxyEdu.Server.Hubs;

public class ProxyHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", new { message = "Conectado ao ProxyEdu Server" });
        await base.OnConnectedAsync();
    }

    public async Task ReportHttpsBlockNotificationFailed(
        string ip,
        string host,
        int port,
        string reason,
        string policy,
        string blockedPageUrl)
    {
        await Clients.All.SendAsync("HttpsBlockNotificationFailed", new
        {
            ip,
            host,
            port,
            reason,
            policy,
            blockedPageUrl,
            blockDestination = "fallback because worker could not open page",
            timestamp = DateTime.UtcNow
        });
    }
}
