using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Data;

public interface IFilterRuleRepository
{
    FilterRule? FindById(string id);
    List<FilterRule> FindAll();
    List<FilterRule> FindActive();
    bool Insert(FilterRule rule);
    bool Update(FilterRule rule);
    bool Delete(string id);
    long Count();
    bool ExistsByPattern(string pattern);
}
