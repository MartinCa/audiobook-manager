using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface IGenreRepository
{
    Task<Genre> GetOrCreateGenre(string name);

    /// <summary>
    /// Batch equivalent of <see cref="GetOrCreateGenre"/>: resolves every distinct name in
    /// <paramref name="names"/> in a single query, creates whichever ones don't already exist
    /// in a single insert, and returns one <see cref="Genre"/> per input name (duplicates in
    /// the input collapse to the same instance).
    /// </summary>
    Task<Dictionary<string, Genre>> GetOrCreateGenres(IEnumerable<string> names);
}
