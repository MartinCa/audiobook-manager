using System.Linq.Expressions;
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
    /// One page of a series' owned books plus the full total, for the series detail's owned
    /// section. Only the fields that section renders are projected, and the page is computed in
    /// SQL with a total order (blank series parts last, then the part, then the book name, then
    /// id) so paging stays stable.
    /// </summary>
    Task<(List<SeriesOwnedBookRow> Items, int Total)> GetSeriesOwnedBooksPageAsync(string seriesName, int skip, int take);

    /// <summary>
    /// Every owned book of one series reduced to its (series part, book name) keys, for the fuzzy
    /// roster reconciliation. Runs only when the per-series reconciliation cache needs refilling;
    /// nothing the matcher does not read is carried. The fetch is bounded to
    /// <paramref name="maxKeys"/> + 1 rows and the caller is told whether that bound was breached,
    /// so a pathological owned set is detected without ever materializing the whole thing; a set
    /// at or under the cap returns complete (its count is the exact owned count).
    /// </summary>
    Task<(List<SeriesOwnedKey> Keys, bool Overflow)> GetSeriesOwnedKeysAsync(string seriesName, int maxKeys);

    /// <summary>
    /// One page of the distinct series values in the library: every distinct non-empty
    /// <see cref="Audiobook.Series"/> tag value, unioned with every row of the series catalog.
    /// The catalog side keeps a series listed whose value no longer appears on any audiobook
    /// (the last owned book was renamed or removed), exactly as the unpaged overview it replaces
    /// did.
    ///
    /// The page and its total are computed in SQL with a single total order (the value itself,
    /// which the UNION makes unique), so paging stays stable however the data shifts between
    /// requests. <paramref name="search"/> folds accents on both sides (the audiobook side uses
    /// the precomputed <c>SeriesFolded</c> column and also matches any author name, mirroring the
    /// old client-side filter). <paramref name="matched"/> narrows to series with (or without) a
    /// catalog row carrying a matched source.
    /// </summary>
    Task<(List<string> Items, int Total)> GetSeriesValuesPageAsync(string? search, bool? matched, int skip, int take);

    /// <summary>Total distinct series values (catalog plus audiobook tags) and how many of those have a matched catalog source, for the overview header badges.</summary>
    Task<(int Total, int Matched)> GetSeriesValueCountsAsync();

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
    Task<(List<(string Series, int BookCount)> Items, int Total)> SearchSeriesAsync(string query, int limit, int offset);
    Task<List<Audiobook>> GetBooksBySeriesAsync(string seriesName, long? authorId);
    Task<List<string>> GetAuthorNamesBySeriesAsync(string seriesName);
    Task<List<string>> GetSeriesNamesAsync();
    Task<string?> GetCoverFilePathAsync(long id);
    Task<(List<(string Series, int BookCount)> Items, int Total)> GetSeriesCountsByAuthorAsync(long authorId, int limit, int offset);
    Task<(List<Audiobook> Items, int Total)> GetStandaloneBooksByAuthorAsync(long authorId, int limit, int offset);
    Task<Audiobook?> GetByIdWithIncludesAsync(long id);
    Task<List<Audiobook>> GetAllWithIncludesAsync();
    Task<List<SeriesGroupingBook>> GetSeriesGroupingDataAsync();
    Task<List<SeriesGroupingBook>> GetSeriesGroupingDataAsync(List<string> seriesValues);

    /// <summary>
    /// One page of the books missing at least one of the caller's selected fields plus the total
    /// count of the same filtered set. The "missing" predicates arrive as SQL-expressible
    /// expressions (built by <c>MissingTagService.Fields</c>, the single source of truth for what
    /// "missing" means) and are applied as an OR of WHERE clauses, so only the page's rows are
    /// projected - no entity graphs, no Description-size blobs. <paramref name="search"/> folds
    /// accents on the precomputed <c>BookNameFolded</c> column; the page is ordered in SQL by
    /// book name then id (a total order, at BINARY-collation cost per the paged-query rule).
    /// </summary>
    Task<(List<MissingTagRow> Items, int Total)> GetMissingTagRowsPageAsync(
        IReadOnlyCollection<Expression<Func<Audiobook, bool>>> missingPredicates,
        string? search,
        int skip,
        int take);

    /// <summary>
    /// All audiobooks reduced to the fields the missing-book candidate search needs. Unlike
    /// <see cref="GetSeriesGroupingDataAsync"/> this includes books with no series value - an
    /// untagged book is a prime candidate for a missing series entry. Pre-filtered by title
    /// similarity to <paramref name="title"/> (accent-insensitive LIKE), bounded to
    /// <paramref name="limit"/> rows so the query never materializes the full table.
    /// </summary>
    Task<List<SeriesCandidateBook>> GetSeriesCandidateDataAsync(string title, int limit);

    /// <summary>
    /// How many books carry each of only the given series values, for the similar-series
    /// detection: the detection shows a book count per candidate, and the page only needs counts
    /// for the candidates it actually returns. Loads no book rows - just a GROUP BY over the
    /// series column.
    /// </summary>
    Task<Dictionary<string, int>> GetSeriesBookCountsAsync(IReadOnlyCollection<string> seriesValues);
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

    /// <summary>
    /// Books that may be due a metadata refresh, projected to the refresh engine's needs (id,
    /// source URL, bookkeeping timestamp): a non-empty Www and either never refreshed or last
    /// refreshed before <paramref name="staleBeforeUtc"/>. Projects in SQL rather than loading
    /// entity graphs - eligibility filtering must not materialize descriptions for books the
    /// caller filters out.
    /// </summary>
    Task<List<MetadataRefreshEligibleBook>> GetBooksEligibleForMetadataRefreshAsync(DateTime? staleBeforeUtc);
    Task UpdateFilePathAsync(long id, string newFullPath, string newFileName);
    Task UpdateLanguageAsync(long id, string? language);
    Task UpdateCoverFilePathAsync(long id, string? coverFilePath);

    /// <summary>Bookkeeping-only column write; see <see cref="UpdateLanguageAsync"/> for why a direct write is safe here.</summary>
    Task UpdateLastMetadataRefreshedAtAsync(long id, DateTime? whenUtc);
    Task DeleteAudiobookAsync(long id);
    Task UpdateAudiobookAsync(Audiobook audiobook);
}
