using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;
public class GenreRepository : IGenreRepository
{
    private readonly DatabaseContext _db;

    public GenreRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<Genre> GetOrCreateGenre(string name)
    {
        var dbGenre = await _db.Genres.SingleOrDefaultAsync(x => x.Name == name)
            ?? new Genre(default, name);

        if (dbGenre.Id == default)
        {
            _db.Genres.Add(dbGenre);
            await _db.SaveChangesAsync();
        }

        return dbGenre;
    }

    public async Task<Dictionary<string, Genre>> GetOrCreateGenres(IEnumerable<string> names)
    {
        var distinctNames = names.Distinct().ToList();
        var result = new Dictionary<string, Genre>();
        if (distinctNames.Count == 0)
        {
            return result;
        }

        var existing = await _db.Genres.Where(g => distinctNames.Contains(g.Name)).ToListAsync();
        foreach (var genre in existing)
        {
            result[genre.Name] = genre;
        }

        var missingNames = distinctNames.Where(n => !result.ContainsKey(n)).ToList();
        if (missingNames.Count > 0)
        {
            var newGenres = missingNames.Select(n => new Genre(default, n)).ToList();
            _db.Genres.AddRange(newGenres);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
            {
                // genres.name is unique, and this reads then inserts across an await on a
                // request-scoped context - the same race PersonRepository.GetOrCreatePersons
                // handles, and for the same reason: organizes run concurrently with interactive
                // saves and with the bulk operations, so two of them can both see a genre as
                // missing and both insert it. The loser adopts the winner's row rather than
                // failing the whole save.
                foreach (var entry in _db.ChangeTracker.Entries<Genre>()
                             .Where(e => e.State == EntityState.Added)
                             .ToList())
                {
                    entry.State = EntityState.Detached;
                }

                var raced = await _db.Genres
                    .Where(g => missingNames.Contains(g.Name))
                    .ToListAsync();

                foreach (var genre in raced)
                {
                    result[genre.Name] = genre;
                }

                if (missingNames.Any(n => !result.ContainsKey(n)))
                {
                    // Some other constraint failed - not the race this handler is for.
                    throw;
                }

                return result;
            }

            foreach (var genre in newGenres)
            {
                result[genre.Name] = genre;
            }
        }

        return result;
    }
}
