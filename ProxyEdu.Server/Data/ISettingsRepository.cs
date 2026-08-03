using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Data;

public interface ISettingsRepository
{
    ProxySettings Get();
    void Save(ProxySettings settings);
}
