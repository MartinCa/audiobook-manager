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

    public async Task<List<string>> GetAuthorNamesAsync()
    {
        return await _db.Persons
            .AsNoTracking()
            .Where(p => p.BooksAuthored.Any())
            .Select(p => p.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();
    }

    public async Task<List<AuthorSummaryRow>> GetAllAuthorSummariesAsync()
    {
        return await _db.Persons
            .AsNoTracking()
            .Where(p => p.BooksAuthored.Any())
            .OrderBy(p => p.Name)
            .Select(p => new AuthorSummaryRow(p.Id, p.Name, p.BooksAuthored.Count))
            .ToListAsync();
    }

    public async Task<List<AuthorSummaryRow>> SearchAuthorSummariesAsync(string query, int limit)
    {
        var pattern = $"%{query}%";

        return await _db.Persons
            .AsNoTracking()
            .Where(p => p.BooksAuthored.Any() && EF.Functions.Like(p.Name, pattern))
            .OrderBy(p => p.Name)
            .Take(limit)
            .Select(p => new AuthorSummaryRow(p.Id, p.Name, p.BooksAuthored.Count))
            .ToListAsync();
    }

    public async Task<AuthorSummaryRow?> GetAuthorSummaryAsync(long authorId)
    {
        return await _db.Persons
            .AsNoTracking()
            .Where(p => p.Id == authorId)
            .Select(p => new AuthorSummaryRow(p.Id, p.Name, p.BooksAuthored.Count))
            .FirstOrDefaultAsync();
    }

    public async Task<Dictionary<string, List<AuthorBookRef>>> GetAuthorBookRefsAsync()
    {
        var rows = await _db.Persons
            .AsNoTracking()
            .Where(p => p.BooksAuthored.Any())
            .Select(p => new
            {
                p.Name,
                Books = p.BooksAuthored.Select(b => new AuthorBookRef(b.Id, b.BookName)).ToList(),
            })
            .ToListAsync();

        return rows
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(r => r.Books).DistinctBy(b => b.Id).ToList(),
                StringComparer.Ordinal);
    }
}
