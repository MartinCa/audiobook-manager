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

    /// <summary>
    /// The genre names for the book-list genre filter's dropdown, sorted alphabetically and
    /// capped at <see cref="GenreRepository.MaxGenreNames"/> (by book count, so the most-used
    /// genres survive the cap) - see AGENTS.md's bounded-list-endpoint invariant.
    /// </summary>
    Task<List<string>> GetAllGenreNamesAsync();
}
