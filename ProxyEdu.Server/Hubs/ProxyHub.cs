using Microsoft.AspNetCore.SignalR;

namespace ProxyEdu.Server.Hubs;

public class ProxyHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", new { message = "Conectado ao ProxyEdu Server" });
        await base.OnConnectedAsync();
    }
}
