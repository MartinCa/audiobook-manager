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

    /// <summary>
    /// Sets the display-time omnibus/box-set inclusion flag on the series row, creating an
    /// unmatched catalog row if none exists yet. Does not touch the stored roster - the full
    /// roster (compilations included) is always stored, and this flag only affects what the
    /// caller considers visible, so no re-fetch is needed here.
    /// </summary>
    Task<Series> SetIncludeOmnibusEditionsAsync(string seriesName, bool includeOmnibusEditions);
}
