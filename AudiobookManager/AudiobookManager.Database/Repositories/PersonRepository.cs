using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public class PersonRepository : IPersonRepository
{
    private readonly DatabaseContext _db;

    public PersonRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<Person> GetOrCreatePerson(string name)
    {
        var dbPerson = _db.Persons.SingleOrDefault(p => p.Name == name)
            ?? new Person(default, name);

        if (dbPerson.Id == default)
        {
            _db.Persons.Add(dbPerson);
            await _db.SaveChangesAsync();
        }

        return dbPerson;
    }
}
