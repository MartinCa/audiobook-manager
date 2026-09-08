using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface ISeriesRepository
{
    Task<List<Series>> GetAllWithExpectedBooksAsync();
    Task<Series?> GetByIdWithExpectedBooksAsync(long id);
    Task<Series?> GetByNameWithExpectedBooksAsync(string name);

    /// <summary>
    /// <see cref="GetByNameWithExpectedBooksAsync"/> for a page of names: the paged series
    /// overview hydrates only the series values on the requested page, so the catalog read is
    /// proportional to the rendered rows rather than the whole catalog.
    /// </summary>
    Task<List<Series>> GetByNamesWithExpectedBooksAsync(List<string> names);

    /// <summary>
    /// The catalog row's metadata only - no roster. The series detail loads this on every page
    /// request for the overview header; the roster itself is only needed when the cached
    /// reconciliation refills, so it must not ride along.
    /// </summary>
    Task<Series?> GetByNameAsync(string name);

    /// <summary>
    /// <see cref="GetByNameWithExpectedBooksAsync"/> for the detail reconciliation: the catalog row
    /// plus its roster, with the roster fetch bounded to <paramref name="maxExpectedBooks"/> + 1
    /// rows. A roster at or under the cap comes back complete; a larger one is detected (the
    /// <c>Overflow</c> flag is set) without materializing/exceeding the cap, so the reconciliation
    /// can fail clearly instead of loading an unbounded set.
    /// </summary>
    Task<(Series? Series, bool Overflow)> GetByNameWithExpectedBooksBoundedAsync(string name, int maxExpectedBooks);
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
