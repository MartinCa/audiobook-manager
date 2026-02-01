using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

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

    public async Task<List<Person>> GetAllAuthorsAsync()
    {
        return await _db.Persons
            .Include(p => p.BooksAuthored)
            .Where(p => p.BooksAuthored.Any())
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<Person?> GetAuthorWithBooksAsync(long authorId)
    {
        return await _db.Persons
            .Include(p => p.BooksAuthored)
                .ThenInclude(a => a.Authors)
            .Include(p => p.BooksAuthored)
                .ThenInclude(a => a.Narrators)
            .Include(p => p.BooksAuthored)
                .ThenInclude(a => a.Genres)
            .FirstOrDefaultAsync(p => p.Id == authorId);
    }
}
