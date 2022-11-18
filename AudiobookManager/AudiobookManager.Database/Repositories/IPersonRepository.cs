using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface IPersonRepository
{
    Task<Person> GetOrCreatePerson(string name);
}
