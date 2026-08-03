using Microsoft.AspNetCore.SignalR;
using ProxyEdu.Server.Services;

namespace ProxyEdu.Server.Hubs;

public class ProxyHub : Hub
{
    private readonly ProxyHubConnectionRegistry _connections;
    public ProxyHub(ProxyHubConnectionRegistry connections) => _connections = connections;

    public override async Task OnConnectedAsync()
    {
        _connections.Connected(Context.ConnectionId);
        await Clients.Caller.SendAsync("Connected", new { message = "Conectado ao ProxyEdu Server" });
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Disconnected(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
