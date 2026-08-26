using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface ISeriesRepository
{
    Task<List<Series>> GetAllWithExpectedBooksAsync();
    Task<Series?> GetByIdWithExpectedBooksAsync(long id);
    Task<Series?> GetByNameWithExpectedBooksAsync(string name);
    Task<Series> UpsertSeriesAsync(Series series);
    Task ReplaceExpectedBooksAsync(long seriesId, List<SeriesExpectedBook> expectedBooks);
    Task<SeriesExpectedBook?> GetExpectedBookAsync(long id);
    Task SetExpectedBookIgnoredAsync(long id, bool ignored);
}
