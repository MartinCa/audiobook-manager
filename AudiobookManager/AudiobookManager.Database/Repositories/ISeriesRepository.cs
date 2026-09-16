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
    /// The names of every matched series, names only. The library-wide series-part-mismatch
    /// sweep consumes the cached per-series reconciliation for exactly these series; unmatched
    /// series have no roster to reconcile against and never produce a part mismatch.
    /// </summary>
    Task<List<string>> GetMatchedSeriesNamesAsync();

    /// <summary>
    /// <see cref="GetByNameWithExpectedBooksAsync"/> for the detail reconciliation: the catalog row
    /// plus its roster, with the roster fetch bounded to <paramref name="maxExpectedBooks"/> + 1
    /// rows. A roster at or under the cap comes back complete; a larger one is detected (the
    /// <c>Overflow</c> flag is set) without materializing/exceeding the cap, so the reconciliation
    /// can fail clearly instead of loading an unbounded set.
    /// </summary>
    Task<(Series? Series, bool Overflow)> GetByNameWithExpectedBooksBoundedAsync(string name, int maxExpectedBooks);
    Task<Series> UpsertSeriesAsync(Series series);

    /// <summary>
    /// Ensures a catalog row exists for <paramref name="name"/> (an unmatched series value has
    /// no catalog row yet, but series-mapping patterns are owned by a Series row, so creating a
    /// mapping for one must create the owning row). Tolerates the read-then-insert race like the
    /// other upserts. Returns the row, existing or freshly inserted, and whether THIS call
    /// inserted it - a freshly-inserted row is safe for the caller to roll back if the
    /// operation that needed it fails; a pre-existing (or race-adopted) row is not.
    /// </summary>
    Task<(Series Series, bool Created)> GetOrCreateByNameAsync(string name);

    /// <summary>
    /// Deletes one catalog row by id. Only the series-mapping create path's rollback uses it,
    /// and only for a row that same call just inserted, so the caller never deletes a row a
    /// concurrent request is building on.
    /// </summary>
    Task<bool> DeleteAsync(long id);

    /// <summary>
    /// Re-keys a catalog row from <paramref name="oldName"/> to <paramref name="newName"/>,
    /// moving the roster (expected-book rows, ignore flags included) and every matched-source
    /// metadata field with it. Used by the source-series-name adoption, which renames every
    /// member book: the catalog row must follow or the old name keeps a matched zombie row
    /// with the whole roster reported missing while the adopted name owns no roster at all.
    /// Only <see cref="Series.Name"/> changes - the row id and its expected-book children are
    /// untouched, so nothing else needs re-pointing. Throws <see cref="KeyNotFoundException"/>
    /// when no row owns the old name, and an <see cref="InvalidOperationException"/> when a row
    /// already owns the new name (a rename would silently merge or clobber its roster).
    /// </summary>
    Task<Series> RenameAsync(string oldName, string newName);
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
