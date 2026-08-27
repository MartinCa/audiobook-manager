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
            await _db.SaveChangesAsync();

            foreach (var genre in newGenres)
            {
                result[genre.Name] = genre;
            }
        }

        return result;
    }
}
