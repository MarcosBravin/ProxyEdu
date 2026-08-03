using ProxyEdu.Server.Security;

namespace ProxyEdu.Server.Data;

public interface IUserRepository
{
    DashboardUser? FindById(string id);
    DashboardUser? FindByUsername(string username);
    List<DashboardUser> FindAll();
    bool Insert(DashboardUser user);
    bool Update(DashboardUser user);
    bool Delete(string id);
    bool Exists(string username);
    long Count();
}
