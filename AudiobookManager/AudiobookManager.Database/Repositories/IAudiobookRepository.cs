using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface IAudiobookRepository
{
    Task<Audiobook> InsertAudiobook(Audiobook audiobook);
    Task<HashSet<string>> GetAllFilePathsAsync(StringComparer? comparer = null);
    Task<Audiobook?> GetByFullPathAsync(string fullPath, Func<string, string, bool>? pathsEqual = null);
    Task<(List<Audiobook> Items, int Total)> GetAllAsync(int limit, int offset);
    Task<int> CountAsync();

    /// <summary>
    /// One page of audiobooks with a *dirty* website URL - one whose query string or fragment
    /// <see cref="AudiobookManager.Scraping.Utils.BookUrlCleaner"/> would strip - projected to the
    /// columns the URL cleanup page renders rather than the full entity graph.
    /// <see cref="GetAllWithIncludesAsync"/> loads Authors, Narrators, Genres and every column just
    /// to read Id, BookName, Authors and Www. The dirty predicate is the cheap SQL mirror of
    /// "the URL has a query or fragment"; the service still runs <c>Clean()</c> per row to build
    /// the cleaned value. Returns the total matching count alongside the page so the caller can
    /// size its pager without a second round trip.
    /// </summary>
    Task<(List<DirtyUrlRow> Items, int Total)> GetDirtyUrlPageAsync(int limit, int offset);
    /// <summary>
    /// <paramref name="includeTotal"/> lets a caller that never renders a total (the type-ahead
    /// endpoint, capped at <paramref name="limit"/>) skip the second full execution of the query
    /// that <c>CountAsync</c> would otherwise cost on every keystroke - <c>Total</c> comes back as
    /// a sentinel <c>0</c> in that case, not a real count.
    /// <paramref name="includeNarratorsAndGenres"/> lets that same caller - which only ever reads
    /// <c>Item.Authors</c> from the result - skip the Narrators/Genres Includes and the split
    /// query they force alongside Authors.
    /// </summary>
    Task<(List<Audiobook> Items, int Total)> SearchAsync(
        string query, int limit, int offset, bool includeTotal = true, bool includeNarratorsAndGenres = true);
    Task<List<(string Series, int BookCount)>> SearchSeriesAsync(string query, int limit);
    Task<List<Audiobook>> GetBooksBySeriesAsync(string seriesName, long? authorId);
    Task<List<string>> GetSeriesNamesAsync();
    Task<string?> GetCoverFilePathAsync(long id);
    Task<List<(string Series, int BookCount)>> GetSeriesCountsByAuthorAsync(long authorId);
    Task<List<Audiobook>> GetStandaloneBooksByAuthorAsync(long authorId);
    Task<Audiobook?> GetByIdWithIncludesAsync(long id);
    Task<List<Audiobook>> GetAllWithIncludesAsync();
    Task<List<SeriesGroupingBook>> GetSeriesGroupingDataAsync();
    Task<Dictionary<string, List<(long Id, string BookName)>>> GetDistinctSeriesAsync();
    Task<List<Audiobook>> GetBooksByAuthorNamesAsync(IEnumerable<string> authorNames);
    Task<List<Audiobook>> GetBooksBySeriesValuesAsync(IEnumerable<string> seriesValues);

    /// <summary>
    /// Every book carrying any of <paramref name="personNames"/> as an author OR a narrator, with
    /// the Authors/Narrators/Genres graph loaded. The initials-spacing resolver needs both roles:
    /// a person value non-compliant with the spacing setting must be rewritten on every book it
    /// appears on, however the book lists it.
    /// </summary>
    Task<List<Audiobook>> GetBooksByPersonNamesAsync(IEnumerable<string> personNames);
    Task<List<AudiobookLanguageRef>> GetBooksMissingLanguageAsync();
    Task UpdateFilePathAsync(long id, string newFullPath, string newFileName);
    Task UpdateLanguageAsync(long id, string? language);
    Task UpdateCoverFilePathAsync(long id, string? coverFilePath);
    Task DeleteAudiobookAsync(long id);
    Task UpdateAudiobookAsync(Audiobook audiobook);
}
