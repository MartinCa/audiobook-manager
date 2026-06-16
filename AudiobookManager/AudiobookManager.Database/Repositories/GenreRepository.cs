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
}
