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
        var dbPerson = await _db.Persons.SingleOrDefaultAsync(p => p.Name == name)
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

    public async Task<List<Person>> SearchAuthorsAsync(string query, int limit)
    {
        var pattern = $"%{query}%";

        return await _db.Persons
            .Include(p => p.BooksAuthored)
            .Where(p => p.BooksAuthored.Any() && EF.Functions.Like(p.Name, pattern))
            .OrderBy(p => p.Name)
            .Take(limit)
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
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == authorId);
    }
}
