using System.Linq.Expressions;
using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface IAudiobookRepository
{
    Task<Audiobook> InsertAudiobook(Audiobook audiobook);
    Task<Audiobook?> GetByFullPathAsync(string fullPath, Func<string, string, bool>? pathsEqual = null);
    Task<(List<Audiobook> Items, int Total)> GetAllAsync(int limit, int offset, BookSummaryFilter? filter = null);
    Task<int> CountAsync();

    /// <summary>Every distinct non-empty <see cref="Audiobook.Language"/> value in the library, sorted for the book-list language filter's dropdown.</summary>
    Task<List<string>> GetAllLanguagesAsync();

    /// <summary>
    /// One page of a series' owned books plus the full total, for the series detail's owned
    /// section. Only the fields that section renders are projected, and the page is computed in
    /// SQL with a total order (blank series parts last, then the part, then the book name, then
    /// id) so paging stays stable. <paramref name="search"/> and <paramref name="filter"/> are
    /// the same accent-folded text search and <see cref="BookSummaryFilter"/> the whole-library
    /// book list applies, scoped to this series' owned books, so the series detail's owned
    /// section gets the same filtering the library and author views offer.
    /// </summary>
    Task<(List<SeriesOwnedBookRow> Items, int Total)> GetSeriesOwnedBooksPageAsync(
        string seriesName, int skip, int take, string? search = null, BookSummaryFilter? filter = null);

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
    ///
    /// <paramref name="authorId"/> scopes the page to the distinct series values of books by that
    /// author alone. An author scope deliberately unions no catalog rows: a series the author
    /// owns no book in is not one the author has, so a catalog-only value would be wrong to list
    /// there. The search/matched filters still apply.
    ///
    /// <paramref name="filter"/> layers the additional followed/owned-count/refreshed-date
    /// filters from <see cref="SeriesOverviewFilter"/> on top; <paramref name="restrictToNames"/>
    /// is how <c>SeriesService</c> applies that filter's missing/upcoming-book fields, which this
    /// repository cannot evaluate itself (see <see cref="SeriesOverviewFilter"/>'s doc) - when
    /// non-null, only series values in this set are returned.
    /// </summary>
    Task<(List<string> Items, int Total)> GetSeriesValuesPageAsync(
        string? search, bool? matched, int skip, int take, long? authorId = null,
        SeriesOverviewFilter? filter = null, IReadOnlyCollection<string>? restrictToNames = null);

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
        string query, int limit, int offset, bool includeTotal = true, bool includeNarratorsAndGenres = true,
        BookSummaryFilter? filter = null);
    Task<(List<(string Series, int BookCount)> Items, int Total)> SearchSeriesAsync(string query, int limit, int offset);
    Task<List<Audiobook>> GetBooksBySeriesAsync(string seriesName, long? authorId);
    Task<List<string>> GetAuthorNamesBySeriesAsync(string seriesName);

    /// <summary>
    /// The series value whose folded name equals the input's folded name, or null - the "this
    /// series already exists" answer for the entry-status classification. Same case- and
    /// accent-insensitivity as every other search in this repository.
    /// </summary>
    Task<string?> FindSeriesValueByFoldedNameAsync(string value);

    /// <summary>
    /// The distinct series values the entry-status classification scores as "similar" candidates,
    /// capped at <paramref name="limit"/> rows - a bounded, deliberately permissive prefilter
    /// (full-query containment ranked ahead of first-token containment) over which the fuzzy
    /// "similar" decision is made. Series values have no identity, so only the strings are returned.
    /// </summary>
    Task<List<string>> SearchSeriesValuesAsync(string query, int limit);

    /// <summary>
    /// Books carrying exactly the given series value whose series part is <em>equivalent</em> to
    /// <paramref name="seriesPart"/> (numeric with rounding, or trimmed case-insensitive text),
    /// excluding <paramref name="excludeAudiobookId"/>. Equivalence is applied in SQL, so the
    /// returned rows are exactly the genuine conflicts - a conflict cannot sort past an
    /// alphabetical bound and be silently missed. Bounded to <paramref name="limit"/> rows with an
    /// explicit truncation flag when more genuine conflicts exist, per the bounded-list invariant.
    /// </summary>
    Task<(List<SeriesPartConflictRow> Items, bool Truncated)> GetSeriesPartConflictCandidatesAsync(
        string series, long excludeAudiobookId, string seriesPart, int limit);

    Task<List<string>> GetSeriesNamesAsync();
    Task<string?> GetCoverFilePathAsync(long id);

    /// <summary>
    /// One page of the author's books that belong to no series, plus the full total.
    /// <paramref name="search"/> and <paramref name="filter"/> are the same accent-folded text
    /// search and <see cref="BookSummaryFilter"/> the whole-library book list applies, scoped to
    /// this author's standalone books, so the author detail's standalone section gets the same
    /// filtering the library and series views offer.
    /// </summary>
    Task<(List<Audiobook> Items, int Total)> GetStandaloneBooksByAuthorAsync(
        long authorId, int limit, int offset, string? search = null, BookSummaryFilter? filter = null);

    /// <summary>
    /// Every owned book of one author reduced to its (series value, series part, book name, id)
    /// keys - the author reconciliation's owned-book input. Unlike the series counterpart, this
    /// deliberately spans the author's whole catalogue including series books: a series-linked
    /// expected entry must be matched against the owned books of the same local series (and an
    /// unmatched source-series entry best-effort matched by position/title over everything the
    /// author owns). Bounded the same way as <see cref="GetSeriesOwnedKeysAsync"/>: at most
    /// <paramref name="maxKeys"/> + 1 rows, with the overflow flag telling the caller whether the
    /// cap was breached.
    /// </summary>
    Task<(List<SeriesOwnedKey> Keys, bool Overflow)> GetOwnedKeysByAuthorAsync(long authorId, int maxKeys);

    /// <summary>
    /// The batched counterpart of <see cref="GetOwnedKeysByAuthorAsync"/> for the bulk authors-list
    /// filter: every owned book of every person in <paramref name="personIds"/>, reduced to an
    /// <see cref="AuthorOwnedKey"/> (person id + the same series/part/title/id key), fetched in
    /// ONE SQL query (a join through the authors many-to-many) ordered by person id then audiobook
    /// id - a total order, so the per-author grouping is stable. Bounded like every owned-key
    /// read: at most <paramref name="maxTotalKeys"/> + 1 rows, with the overflow flag telling the
    /// caller whether the total was breached. Below the bound every requested person's key set is
    /// complete; past it the flat bound can cut an author's keys mid-list, so the caller must
    /// treat the result as untrustworthy rather than classify from a short prefix.
    /// </summary>
    Task<(List<AuthorOwnedKey> Keys, bool Overflow)> GetOwnedKeysByAuthorsAsync(
        IReadOnlyList<long> personIds, int maxTotalKeys);

    Task<Audiobook?> GetByIdWithIncludesAsync(long id);

    /// <summary>
    /// The audiobooks whose ids are in <paramref name="ids"/>, sorted by id, with the
    /// Authors/Narrators/Genres graph loaded. The bulk edit/refresh/check flows select a set of
    /// books and work on domain copies of them; <c>AsNoTracking</c> is deliberate - those flows
    /// hold the per-audiobook save gate and hand every change to
    /// <see cref="AudiobookService.UpdateAudiobook"/>, which reloads the tracked entity itself.
    /// </summary>
    Task<List<Audiobook>> GetByIdsWithIncludesAsync(IReadOnlyList<long> ids);
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
