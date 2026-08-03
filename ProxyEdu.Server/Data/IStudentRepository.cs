using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Data;

public interface IStudentRepository
{
    StudentInfo? FindById(string id);
    StudentInfo? FindByIp(string ipAddress);
    List<StudentInfo> FindAll();
    List<StudentInfo> FindByGroup(string groupName);
    List<StudentInfo> FindConnected();
    void Insert(StudentInfo student);
    bool Update(StudentInfo student);
    bool Delete(string id);
}
