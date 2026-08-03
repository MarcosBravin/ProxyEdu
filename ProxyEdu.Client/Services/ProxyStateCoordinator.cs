namespace ProxyEdu.Client.Services;

/// <summary>
/// Coordenador de estado do proxy entre ProxyClientService e ProxyProtectionService.
/// Resolve race conditions onde um serviço configura e outro desconfigura o proxy.
/// </summary>
public static class ProxyStateCoordinator
{
    private static readonly object _lock = new();
    private static bool _proxyEnabled;
    private static string? _currentProxyAddress;
    private static bool _intentionalFailOpen;
    private static DateTime _lastStateChange = DateTime.UtcNow;

    /// <summary>
    /// O ProxyClientService chama este método para indicar que o proxy
    /// deve estar ativo com o endereço especificado.
    /// </summary>
    public static void SetProxyEnabled(string proxyAddress)
    {
        lock (_lock)
        {
            _proxyEnabled = true;
            _currentProxyAddress = proxyAddress;
            _intentionalFailOpen = false;
            _lastStateChange = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// O ProxyClientService chama este método para indicar fail-open intencional
    /// (servidor offline). O ProxyProtectionService deve respeitar este estado.
    /// </summary>
    public static void SetProxyDisabled(string? reason = null)
    {
        lock (_lock)
        {
            _proxyEnabled = false;
            _intentionalFailOpen = true;
            _lastStateChange = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Retorna se o proxy deve estar ativo atualmente.
    /// </summary>
    public static (bool enabled, string? address, bool intentionalFailOpen) GetCurrentState()
    {
        lock (_lock)
        {
            return (_proxyEnabled, _currentProxyAddress, _intentionalFailOpen);
        }
    }

    /// <summary>
    /// O ProxyProtectionService usa este método para verificar se deve
    /// forçar o proxy. Respeita o fail-open intencional do Client.
    /// </summary>
    public static bool ShouldProtectionEnforce()
    {
        lock (_lock)
        {
            // Se o Client intencionalmente entrou em fail-open, não forçar
            if (_intentionalFailOpen)
            {
                return false;
            }
            // Se o proxy já está habilitado com endereço conhecido, manter
            return _proxyEnabled && !string.IsNullOrEmpty(_currentProxyAddress);
        }
    }
}

