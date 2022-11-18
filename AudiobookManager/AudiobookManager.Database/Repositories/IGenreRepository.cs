using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface IGenreRepository
{
    Task<Genre> GetOrCreateGenre(string name);
}
