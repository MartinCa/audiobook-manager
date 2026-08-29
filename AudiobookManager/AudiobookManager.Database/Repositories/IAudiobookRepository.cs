using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface IAudiobookRepository
{
    Task<Audiobook> InsertAudiobook(Audiobook audiobook);
    Task<HashSet<string>> GetAllFilePathsAsync(StringComparer? comparer = null);
    Task<Audiobook?> GetByFullPathAsync(string fullPath, Func<string, string, bool>? pathsEqual = null);
    Task<(List<Audiobook> Items, int Total)> GetAllAsync(int limit, int offset);
    Task<(List<Audiobook> Items, int Total)> SearchAsync(string query, int limit, int offset);
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
    Task UpdateFilePathAsync(long id, string newFullPath, string newFileName);
    Task UpdateCoverFilePathAsync(long id, string? coverFilePath);
    Task DeleteAudiobookAsync(long id);
    Task UpdateAudiobookAsync(Audiobook audiobook);
}
