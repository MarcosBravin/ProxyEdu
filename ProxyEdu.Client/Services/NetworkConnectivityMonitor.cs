using System.Net.NetworkInformation;

namespace ProxyEdu.Client.Services;

/// <summary>Expõe mudanças de rede como uma geração monotônica para todos os workers.</summary>
public sealed class NetworkConnectivityMonitor : IHostedService, IDisposable
{
    private readonly ILogger<NetworkConnectivityMonitor> _logger;
    private long _generation;

    public NetworkConnectivityMonitor(ILogger<NetworkConnectivityMonitor> logger) => _logger = logger;

    public long Generation => Interlocked.Read(ref _generation);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
        return Task.CompletedTask;
    }

    private void OnNetworkChanged(object? sender, EventArgs args)
    {
        var generation = Interlocked.Increment(ref _generation);
        _logger.LogInformation("Mudança de endereço de rede detectada; geração {Generation}", generation);
    }

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args)
    {
        var generation = Interlocked.Increment(ref _generation);
        _logger.LogInformation("Disponibilidade da rede alterada para {Available}; geração {Generation}", args.IsAvailable, generation);
    }

    public void Dispose() => StopAsync(CancellationToken.None).GetAwaiter().GetResult();
}
