using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Data;

public interface ILogRepository
{
    void Insert(AccessLog log);
    (List<AccessLog> Items, int Total) Query(
        string? studentId = null,
        string? domain = null,
        bool? blocked = null,
        int page = 1,
        int pageSize = 50);
    List<AccessLog> GetRecent(int count = 20);
    void DeleteAll();
    void DeleteByStudent(string studentId);
    void DeleteOlderThan(DateTime cutoff);
    long Count();
    long CountBlocked();
    List<TopDomain> GetTopDomains(int count = 10);
}
