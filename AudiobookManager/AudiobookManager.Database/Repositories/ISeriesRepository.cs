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
    /// Resolves a roster entry by its natural key (series name plus position and/or title), using
    /// the same matching rule <see cref="SetExpectedBookIgnoredAsync"/> applies: prefer an entry
    /// matching both parts of the key, then fall back to either alone. Returns null when the
    /// series or the entry is not found, so callers decide how to report it.
    /// </summary>
    Task<SeriesExpectedBook?> FindExpectedBookAsync(string seriesName, string? position, string? title);

    /// <summary>
    /// Resolves a roster entry by its natural key using strict matching: when both position
    /// and title are supplied, one row must match both (no fallback to either alone); when
    /// only one is supplied, one row must match that field. Returns null when the series or
    /// the entry is not found.
    /// </summary>
    Task<SeriesExpectedBook?> FindExpectedBookStrictAsync(string seriesName, string? position, string? title);

    /// <summary>
    /// Sets the display-time omnibus/box-set inclusion flag on the series row, creating an
    /// unmatched catalog row if none exists yet. Does not touch the stored roster - the full
    /// roster (compilations included) is always stored, and this flag only affects what the
    /// caller considers visible, so no re-fetch is needed here.
    /// </summary>
    Task<Series> SetIncludeOmnibusEditionsAsync(string seriesName, bool includeOmnibusEditions);
}
