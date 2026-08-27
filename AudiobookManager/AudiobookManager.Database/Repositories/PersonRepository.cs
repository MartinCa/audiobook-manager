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

    public async Task<Dictionary<string, Person>> GetOrCreatePersons(IEnumerable<string> names)
    {
        var distinctNames = names.Distinct().ToList();
        var result = new Dictionary<string, Person>();
        if (distinctNames.Count == 0)
        {
            return result;
        }

        var existing = await _db.Persons.Where(p => distinctNames.Contains(p.Name)).ToListAsync();
        foreach (var person in existing)
        {
            result[person.Name] = person;
        }

        var missingNames = distinctNames.Where(n => !result.ContainsKey(n)).ToList();
        if (missingNames.Count > 0)
        {
            var newPersons = missingNames.Select(n => new Person(default, n)).ToList();
            _db.Persons.AddRange(newPersons);
            await _db.SaveChangesAsync();

            foreach (var person in newPersons)
            {
                result[person.Name] = person;
            }
        }

        return result;
    }

    public async Task<List<Person>> GetAllAuthorsAsync()
    {
        return await _db.Persons
            .AsNoTracking()
            .Include(p => p.BooksAuthored)
            .Where(p => p.BooksAuthored.Any())
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<List<Person>> SearchAuthorsAsync(string query, int limit)
    {
        var pattern = $"%{query}%";

        return await _db.Persons
            .AsNoTracking()
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
