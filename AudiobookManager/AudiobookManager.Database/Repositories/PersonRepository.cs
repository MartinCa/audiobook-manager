using AudiobookManager.Database.Models;
using AudiobookManager.Database.Search;
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

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
            {
                // persons.name is unique, and this method reads then inserts across an await
                // from a request-scoped context. Organizes genuinely run concurrently (the bulk
                // import fans out, OrganizeWorker runs alongside an interactive save), so two
                // of them can both see a new author as missing and both insert it. The loser of
                // that race must adopt the winner's row rather than failing the whole organize
                // and leaving the file half-processed.
                foreach (var entry in _db.ChangeTracker.Entries<Person>()
                             .Where(e => e.State == EntityState.Added)
                             .ToList())
                {
                    entry.State = EntityState.Detached;
                }

                var raced = await _db.Persons
                    .Where(p => missingNames.Contains(p.Name))
                    .ToListAsync();

                foreach (var person in raced)
                {
                    result[person.Name] = person;
                }

                if (missingNames.Any(n => !result.ContainsKey(n)))
                {
                    // Some other constraint failed - not the race this handler is for.
                    throw;
                }

                return result;
            }

            foreach (var person in newPersons)
            {
                result[person.Name] = person;
            }
        }

        return result;
    }

    public async Task<List<string>> GetAuthorNamesAsync()
    {
        // Project in SQL, but sort in memory. SQLite's default BINARY collation orders by code
        // point, so "Zadie" sorts before "alice" and every accented name lands after "Z" - which
        // is user-visible nonsense in the pick-from-a-list autocomplete this feeds. The row set
        // is a flat list of distinct names, so sorting it here costs nothing.
        var names = await _db.Persons
            .AsNoTracking()
            .Where(p => p.BooksAuthored.Any())
            .Select(p => p.Name)
            .Distinct()
            .ToListAsync();

        names.Sort(StringComparer.InvariantCulture);
        return names;
    }

    public async Task<List<string>> GetNarratorNamesAsync()
    {
        // Same reasoning (and the same comparer) as GetAuthorNamesAsync: sort in memory rather
        // than in SQL so the ordering is culture-aware, not SQLite's code-point BINARY collation.
        var names = await _db.Persons
            .AsNoTracking()
            .Where(p => p.BooksNarrated.Any())
            .Select(p => p.Name)
            .Distinct()
            .ToListAsync();

        names.Sort(StringComparer.InvariantCulture);
        return names;
    }

    public async Task<List<AuthorSummaryRow>> GetAllAuthorSummariesAsync()
    {
        var rows = await _db.Persons
            .AsNoTracking()
            .Where(p => p.BooksAuthored.Any())
            .Select(p => new AuthorSummaryRow(p.Id, p.Name, p.BooksAuthored.Count))
            .ToListAsync();

        // Project in SQL, order in memory. This list is unpaged, so nothing forces the sort
        // into SQL - and SQLite's BINARY collation would order it by code point, putting
        // "Zadie" before "alice" and every accented surname after "Z". Same reasoning (and the
        // same comparer) as GetAuthorNamesAsync.
        return rows.OrderBy(r => r.Name, StringComparer.InvariantCulture).ToList();
    }

    public async Task<(List<AuthorSummaryRow> Items, int Total)> GetAuthorSummariesPagedAsync(
        string? search, int limit, int offset)
    {
        var dbQuery = _db.Persons
            .AsNoTracking()
            .Where(p => p.BooksAuthored.Any());

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Folded on the precomputed NameFolded column like every other search in this
            // repository; the page is ordered in SQL, so it gets BINARY collation (the
            // documented tradeoff for a paged query - see the ordering rule in AGENTS.md).
            var pattern = $"%{AccentFolding.FoldPlain(search!.Trim())}%";
            dbQuery = dbQuery.Where(p => EF.Functions.Like(p.NameFolded, pattern));
        }

        var total = await dbQuery.CountAsync();

        // BookCount (BooksAuthored.Count) is part of the total order too, defensively: persons
        // names are unique, but the issue asked for it and a future without the unique index
        // must not silently get a non-total order. Id is the final tiebreaker that makes the
        // order total.
        var rows = await dbQuery
            .OrderBy(p => p.Name)
            .ThenByDescending(p => p.BooksAuthored.Count)
            .ThenBy(p => p.Id)
            .Skip(offset)
            .Take(limit)
            .Select(p => new AuthorSummaryRow(p.Id, p.Name, p.BooksAuthored.Count))
            .ToListAsync();

        return (rows, total);
    }

    public async Task<(List<AuthorSummaryRow> Items, int Total)> SearchAuthorSummariesAsync(string query, int limit, int offset)
    {
        var folded = AccentFolding.FoldPlain(query);
        var pattern = $"%{folded}%";
        var prefixPattern = $"{folded}%";

        var dbQuery = _db.Persons
            .AsNoTracking()
            .Where(p => p.BooksAuthored.Any() && EF.Functions.Like(p.NameFolded, pattern));

        var total = await dbQuery.CountAsync();

        var rows = await dbQuery
            // Rank before the limit. This query is capped at `limit` rows, so ordering
            // alphabetically and re-ranking the survivors in the controller discarded the
            // prefix matches the user was most likely reaching for - see SearchAsync.
            .OrderByDescending(p => EF.Functions.Like(p.NameFolded, prefixPattern))
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip(offset)
            .Take(limit)
            .Select(p => new AuthorSummaryRow(p.Id, p.Name, p.BooksAuthored.Count))
            .ToListAsync();

        return (rows, total);
    }

    public async Task<AuthorSummaryRow?> GetAuthorSummaryAsync(long authorId)
    {
        return await _db.Persons
            .AsNoTracking()
            .Where(p => p.Id == authorId)
            .Select(p => new AuthorSummaryRow(p.Id, p.Name, p.BooksAuthored.Count))
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Author name -> how many books carry that author name, for only the names in
    /// <paramref name="authorNames"/>. The similar-author detection pages its groups, so only
    /// the current page's candidate names need counts - the old implementation loaded every
    /// author's full book list to derive the same numbers.
    /// </summary>
    public async Task<Dictionary<string, int>> GetAuthorBookCountsAsync(IReadOnlyCollection<string> authorNames)
    {
        if (authorNames.Count == 0)
        {
            return new Dictionary<string, int>();
        }

        var rows = await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Authors.Any(p => authorNames.Contains(p.Name)))
            .SelectMany(a => a.Authors)
            .Where(p => authorNames.Contains(p.Name))
            .GroupBy(p => p.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync();

        return rows.ToDictionary(r => r.Name, r => r.Count, StringComparer.Ordinal);
    }
}
