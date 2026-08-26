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

    /// <summary>
    /// Sets the ignore flag on the roster entry addressed by series name plus position
    /// and/or title. Addressing by natural key rather than row id, because the roster is
    /// deleted and re-inserted on every match/refresh.
    /// </summary>
    Task SetExpectedBookIgnoredAsync(string seriesName, string? position, string? title, bool ignored);
}
