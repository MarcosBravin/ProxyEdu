using System.Collections.Concurrent;

namespace ProxyEdu.Server.Services;

public sealed class ProxyHubConnectionRegistry
{
    private readonly ConcurrentDictionary<string, DateTime> _connections = new(StringComparer.Ordinal);
    public void Connected(string connectionId) => _connections[connectionId] = DateTime.UtcNow;
    public void Disconnected(string connectionId) => _connections.TryRemove(connectionId, out _);
    public int Count => _connections.Count;
}
