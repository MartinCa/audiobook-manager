using System.Linq.Expressions;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Search;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;
public class AudiobookRepository : IAudiobookRepository
{
    private readonly DatabaseContext _db;

    public AudiobookRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<Audiobook> InsertAudiobook(Audiobook audiobook)
    {
        _db.Add(audiobook);
        await _db.SaveChangesAsync();
        return audiobook;
    }

    public async Task<HashSet<string>> GetAllFilePathsAsync(StringComparer? comparer = null)
    {
        var paths = await _db.Audiobooks.AsNoTracking().Select(a => a.FileInfoFullPath).ToListAsync();
        return paths.ToHashSet(comparer ?? StringComparer.Ordinal);
    }

    /// <summary>
    /// The tracked book at <paramref name="fullPath"/>, if any.
    ///
    /// Path comparison is a property of the file system, not of the string, so it cannot be done
    /// in SQL - SQLite's BINARY collation would treat a case-only difference as a different file
    /// even on Windows/macOS, and it normalizes nothing. This narrows in SQL on the file name
    /// (indexed) and settles it in memory with the caller's own comparison, exactly as
    /// <see cref="GetAllFilePathsAsync"/> takes its comparer from the caller. The Database layer
    /// deliberately has no reference to FileManager, so the predicate comes in rather than being
    /// hard-coded to AudiobookFileHandler.PathsEqual.
    /// </summary>
    public async Task<Audiobook?> GetByFullPathAsync(string fullPath, Func<string, string, bool>? pathsEqual = null)
    {
        var fileName = Path.GetFileName(fullPath);

        // LIKE with no wildcards is an equality test that SQLite evaluates case-insensitively
        // for ASCII, which is what catches a case-only difference on a case-insensitive volume.
        // The escape keeps a literal '_' or '%' in a file name (both common) from turning into
        // a wildcard; a slightly wider candidate set would be harmless - the predicate below
        // still decides - but not a free one.
        var likePattern = EscapeLikePattern(fileName);

        // Only the id and the path, not the rows: file names are not unique (a library where
        // every file is "audiobook.m4b" is unusual but perfectly legal), and materializing every
        // same-named book - Description blobs and all - to compare one string would be a poor
        // trade for the one row that matches.
        var candidates = await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.FileInfoFileName == fileName
                || EF.Functions.Like(a.FileInfoFileName, likePattern, LikeEscapeCharacter))
            .Select(a => new { a.Id, a.FileInfoFullPath })
            .ToListAsync();

        var match = pathsEqual is null
            ? candidates.FirstOrDefault(a => string.Equals(a.FileInfoFullPath, fullPath, StringComparison.Ordinal))
            : candidates.FirstOrDefault(a => pathsEqual(a.FileInfoFullPath, fullPath));

        if (match is null)
        {
            return null;
        }

        return await _db.Audiobooks.AsNoTracking().FirstOrDefaultAsync(a => a.Id == match.Id);
    }

    private const string LikeEscapeCharacter = "\\";

    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    /// <summary>How many books the library tracks. Just the count - no rows materialized.</summary>
    public Task<int> CountAsync() => _db.Audiobooks.AsNoTracking().CountAsync();

    /// <summary>
    /// What the URL cleanup page calls "dirty": a URL BookUrlCleaner.Clean would change. The SQL
    /// mirror has to agree with Clean() in the direction that matters - it must not flag a value
    /// Clean() would leave alone - so a query string or fragment is only "dirty" when it follows a
    /// parseable absolute URL (a scheme separator). A hand-edited scheme-less value like
    /// <c>www.audible.com/pd/X?ref=y</c> fails Uri.TryCreate, Clean() returns it unchanged, and
    /// flagging it anyway would render a card whose struck-through and green URLs are the same
    /// string and keep the list permanently non-empty.
    ///
    /// Clean() also normalizes default ports and scheme/host casing, which this predicate does not
    /// express (rare, and the tool is about tracking parameters) - accepted as a small false-negative
    /// trade-off, asserted by GetDirtyUrlPageAsync tests.
    /// </summary>
    private static readonly Expression<Func<Audiobook, bool>> IsDirtyUrl = a =>
        a.Www != null &&
        a.Www != "" &&
        a.Www.Contains("://") &&
        (a.Www.IndexOf('?') > a.Www.IndexOf("://") || a.Www.IndexOf('#') > a.Www.IndexOf("://"));

    public async Task<(List<DirtyUrlRow> Items, int Total)> GetDirtyUrlPageAsync(int limit, int offset)
    {
        var dirty = _db.Audiobooks
            .AsNoTracking()
            .Where(IsDirtyUrl);

        var total = await dirty.CountAsync();

        // The same total order as GetAllWithIncludesAsync. It has to be total, not just by
        // BookName: SQLite is free to return ties in any order, and two pages ordered only
        // partially can repeat a row and drop another.
        var items = await dirty
            .OrderBy(a => a.BookName).ThenBy(a => a.Id)
            .Skip(offset)
            .Take(limit)
            .Select(a => new DirtyUrlRow(a.Id, a.BookName, a.Authors.Select(p => p.Name).ToList(), a.Www!))
            .ToListAsync();

        return (items, total);
    }

    public async Task<(List<Audiobook> Items, int Total)> GetAllAsync(int limit, int offset)
    {
        var query = _db.Audiobooks
            .AsNoTracking()
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres.OrderBy(g => g.Name))
            .AsSplitQuery()
            // BookName is not unique, so it cannot order a page on its own: rows sharing a
            // title have an undefined relative order, which lets the same book appear on two
            // pages (and another be skipped) - and with AsSplitQuery the Skip/Take runs in each
            // of the queries, so they can even disagree about which rows the page contains.
            .OrderBy(a => a.BookName).ThenBy(a => a.Id);

        var total = await query.CountAsync();
        var items = await query.Skip(offset).Take(limit).ToListAsync();
        return (items, total);
    }

    /// <summary>
    /// <paramref name="includeTotal"/> false makes <c>Total</c> a sentinel <c>0</c>, not a real
    /// count - only meaningful for a caller that never reads it (the type-ahead path below).
    /// <paramref name="includeNarratorsAndGenres"/> false skips those two Includes/the resulting
    /// split query, for a caller that - like that same type-ahead path - only reads
    /// <c>a.Authors</c> from the result.
    /// </summary>
    public async Task<(List<Audiobook> Items, int Total)> SearchAsync(
        string query, int limit, int offset, bool includeTotal = true, bool includeNarratorsAndGenres = true)
    {
        // Fold the query so an unaccented search (e.g. "Rene") still matches an accented value
        // ("René") - SQLite's default BINARY collation, which LIKE uses here, never does that.
        // The columns below are already folded (see BookNameFolded etc. on the Audiobook/Person
        // models - plain columns kept in sync by AccentFoldedColumnsInterceptor) rather than
        // folded per row at query time: wrapping the source column in fold_accents() here, as this
        // used to, cost a callback into managed code for every row scanned, on every OR term, on
        // every keystroke (#1303).
        var folded = AccentFolding.FoldPlain(query);
        var pattern = $"%{folded}%";
        var prefixPattern = $"{folded}%";

        // Authors is unconditional - every caller ranks on it (see the OrderByDescending below)
        // and the type-ahead path renders it. Narrators/Genres are pulled in (as a split query, to
        // avoid the row-multiplication a second and third collection Include would cause combined
        // with Authors) only for a caller that actually reads them: BrowseController.SearchAudiobooks
        // maps every hit through MapToSummaryDto, which does; SearchLibrary's type-ahead only ever
        // reads a.Authors from its hits, so those two Includes plus the split query they force were
        // pure overhead on every keystroke for a value never rendered.
        IQueryable<Audiobook> baseQuery = _db.Audiobooks.AsNoTracking().Include(a => a.Authors);
        if (includeNarratorsAndGenres)
        {
            baseQuery = baseQuery.Include(a => a.Narrators).Include(a => a.Genres.OrderBy(g => g.Name)).AsSplitQuery();
        }

        var dbQuery = baseQuery
            .Where(a =>
                EF.Functions.Like(a.BookNameFolded, pattern) ||
                EF.Functions.Like(a.SubtitleFolded, pattern) ||
                EF.Functions.Like(a.SeriesFolded, pattern) ||
                a.Authors.Any(p => EF.Functions.Like(p.NameFolded, pattern)) ||
                EF.Functions.Like(a.DescriptionFolded, pattern)
            )
            // Rank in SQL, before Skip/Take. Ordering by title alone and ranking the survivors
            // in the controller meant a limit-5 type-ahead kept the five alphabetically-first
            // matches and re-ranked those - so searching "harry" in a library holding "Alex
            // Rider" ... "Harry Potter" never surfaced the one title that actually starts with
            // it. ThenBy(Id) keeps the order total, which a paged split query requires.
            .OrderByDescending(a =>
                EF.Functions.Like(a.BookNameFolded, prefixPattern) ||
                a.Authors.Any(p => EF.Functions.Like(p.NameFolded, prefixPattern)))
            .ThenBy(a => a.BookName)
            .ThenBy(a => a.Id);

        // The type-ahead path (BrowseController.SearchLibrary) is capped at `limit` and never
        // renders a total, so counting there was a second full execution of the query for a
        // number nothing displays.
        var total = includeTotal ? await dbQuery.CountAsync() : 0;
        var items = await dbQuery.Skip(offset).Take(limit).ToListAsync();
        return (items, total);
    }

    public async Task<(List<(string Series, int BookCount)> Items, int Total)> SearchSeriesAsync(string query, int limit, int offset)
    {
        var folded = AccentFolding.FoldPlain(query);
        var pattern = $"%{folded}%";
        var prefixPattern = $"{folded}%";

        var matching = _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series != null && a.Series != "" && EF.Functions.Like(a.SeriesFolded, pattern));

        var total = await matching.Select(a => a.Series!).Distinct().CountAsync();

        var rows = await matching
            .GroupBy(a => a.Series!)
            .Select(g => new { Series = g.Key, BookCount = g.Count() })
            // Rank before the limit, not after it - see SearchAsync for what ranking the
            // survivors of an alphabetical Take costs.
            .OrderByDescending(g => EF.Functions.Like(AccentFolding.Fold(g.Series), prefixPattern))
            .ThenBy(g => g.Series)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return (rows.Select(r => (r.Series, r.BookCount)).ToList(), total);
    }

    public async Task<List<Audiobook>> GetBooksBySeriesAsync(string seriesName, long? authorId)
    {
        var query = _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres.OrderBy(g => g.Name))
            .AsSplitQuery()
            .Where(a => a.Series == seriesName);

        if (authorId.HasValue)
        {
            query = query.Where(a => a.Authors.Any(p => p.Id == authorId.Value));
        }

        return await query.OrderBy(a => a.SeriesPart).ThenBy(a => a.Id).ToListAsync();
    }

    public async Task<List<string>> GetAuthorNamesBySeriesAsync(string seriesName)
    {
        var names = await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series == seriesName)
            .SelectMany(a => a.Authors.Select(p => p.Name))
            .Distinct()
            .ToListAsync();

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// One page of a series' owned books plus the full total, for the series detail's owned
    /// section. Only the fields that section renders are projected (author/narrator names as
    /// correlated subqueries), and the page is computed in SQL with a total order - blank series
    /// parts last, then the part, then the book name, then id - so paging stays stable while the
    /// library grows. This replaced a per-section read of every owned book with its Genres and
    /// Description for a view that shows a page at a time.
    /// </summary>
    public async Task<(List<SeriesOwnedBookRow> Items, int Total)> GetSeriesOwnedBooksPageAsync(
        string seriesName, int skip, int take)
    {
        var query = _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series == seriesName);

        var total = await query.CountAsync();

        var items = await query
            .OrderBy(a => a.SeriesPart == null || a.SeriesPart.Trim() == "" ? 1 : 0)
            .ThenBy(a => a.SeriesPart)
            .ThenBy(a => a.BookName)
            .ThenBy(a => a.Id)
            .Skip(skip)
            .Take(take)
            .Select(a => new SeriesOwnedBookRow(
                a.Id,
                a.BookName,
                a.SeriesPart,
                a.Year,
                a.Authors.Select(p => p.Name).ToList(),
                a.Narrators.Select(p => p.Name).ToList(),
                a.DurationInSeconds))
            .ToListAsync();

        return (items, total);
    }

    /// <summary>
    /// Every owned book of one series reduced to its (series part, book name) keys. The series
    /// detail reconciles a roster against these, but that reconciliation is cached per series;
    /// this runs only when the cache needs refilling, and carries nothing the fuzzy matcher does
    /// not read - no authors, no ids, no entity graph.
    ///
    /// The fetch is bounded to <paramref name="maxKeys"/> + 1 rows and returns whether that bound
    /// was breached, so a pathological owned set is detected without ever materializing (or
    /// transferring) the whole thing; a set at or under the cap comes back complete - the returned
    /// count is then the exact owned count the reconciliation needs, no second query required.
    /// </summary>
    public async Task<(List<SeriesOwnedKey> Keys, bool Overflow)> GetSeriesOwnedKeysAsync(
        string seriesName, int maxKeys)
    {
        var rows = await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series == seriesName)
            .OrderBy(a => a.Id)
            .Take(maxKeys + 1)
            .Select(a => new { a.SeriesPart, a.BookName })
            .ToListAsync();

        return (rows.Select(r => new SeriesOwnedKey(r.SeriesPart, r.BookName)).ToList(), rows.Count > maxKeys);
    }

    /// <summary>
    /// Just the cover path for one book. The cover endpoint used to load the whole entity with
    /// its authors/narrators/genres - three extra split queries - to read this one column.
    /// </summary>
    public async Task<string?> GetCoverFilePathAsync(long id)
    {
        return await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => a.CoverFilePath)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// One page of per-series book counts for one author, aggregated in SQL. The author detail
    /// view only renders a name and a count for each series, so the books themselves are never
    /// loaded. Paged with a total order (the series name, which the GROUP BY makes unique within
    /// an author's series) so page boundaries stay stable.
    /// </summary>
    public async Task<(List<(string Series, int BookCount)> Items, int Total)> GetSeriesCountsByAuthorAsync(
        long authorId, int limit, int offset)
    {
        var matching = _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series != null && a.Series != "" && a.Authors.Any(p => p.Id == authorId));

        var total = await matching.Select(a => a.Series!).Distinct().CountAsync();

        var rows = await matching
            .GroupBy(a => a.Series!)
            .Select(g => new { Series = g.Key, BookCount = g.Count() })
            .OrderBy(g => g.Series)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return (rows.Select(r => (r.Series, r.BookCount)).ToList(), total);
    }

    /// <summary>One page of the author's books that belong to no series, plus the full total.</summary>
    public async Task<(List<Audiobook> Items, int Total)> GetStandaloneBooksByAuthorAsync(
        long authorId, int limit, int offset)
    {
        var matching = _db.Audiobooks
            .AsNoTracking()
            .Where(a => (a.Series == null || a.Series == "") && a.Authors.Any(p => p.Id == authorId));

        var total = await matching.CountAsync();

        // Ordered in SQL because the query is paged - the documented tradeoff for a paged query
        // is BINARY collation over the name; id is the tiebreaker that makes the order total.
        var books = await matching
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres.OrderBy(g => g.Name))
            .AsSplitQuery()
            .OrderBy(a => a.BookName)
            .ThenBy(a => a.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return (books, total);
    }

    public async Task<Audiobook?> GetByIdWithIncludesAsync(long id)
    {
        return await _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres.OrderBy(g => g.Name))
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    /// <inheritdoc cref="IAudiobookRepository.GetByIdsWithIncludesAsync"/>
    public async Task<List<Audiobook>> GetByIdsWithIncludesAsync(IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
        {
            return new List<Audiobook>();
        }

        return await _db.Audiobooks
            .AsNoTracking()
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres.OrderBy(g => g.Name))
            .AsSplitQuery()
            .Where(a => ids.Contains(a.Id))
            .OrderBy(a => a.Id)
            .ToListAsync();
    }

    public async Task<List<Audiobook>> GetAllWithIncludesAsync()
    {
        return await _db.Audiobooks
            .AsNoTracking()
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres.OrderBy(g => g.Name))
            .AsSplitQuery()
            .OrderBy(a => a.BookName).ThenBy(a => a.Id)
            .ToListAsync();
    }

    /// <summary>
    /// One page of books missing at least one of the fields the caller selected, plus the total
    /// count of the same filtered set. Unlike the unpaged projection it replaces, the selected
    /// fields' "is missing" predicates are applied as a WHERE clause in SQL (an OR of the
    /// <see cref="Expression{TDelegate}"/>s <paramref name="missingPredicates"/> carries, which
    /// Microsoft.EntityFrameworkCore translates into EXISTS/negated-EXISTS subqueries and blank
    /// checks), so a library where half the books lack a Language tag never sends half the
    /// audiobooks over the wire just to compute a page.
    ///
    /// The page is ordered in SQL, which means BINARY collation over the book name - the
    /// documented tradeoff for a paged query (see the ordering rule in AGENTS.md); Id is the
    /// tiebreaker that makes the order total. <paramref name="search"/> folds accents on the
    /// precomputed <c>BookNameFolded</c> column.
    /// </summary>
    public async Task<(List<MissingTagRow> Items, int Total)> GetMissingTagRowsPageAsync(
        IReadOnlyCollection<Expression<Func<Audiobook, bool>>> missingPredicates,
        string? search,
        int skip,
        int take)
    {
        var query = _db.Audiobooks.AsNoTracking();

        // No selected field means no book can be missing one of them - never an unfiltered pass
        // over the whole library (the callers that reach this method always have at least one).
        if (missingPredicates.Count == 0)
        {
            return (new List<MissingTagRow>(), 0);
        }

        query = query.Where(BuildOr(missingPredicates));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{AccentFolding.FoldPlain(search!.Trim())}%";
            query = query.Where(a => EF.Functions.Like(a.BookNameFolded, pattern));
        }

        // The matching set is answered in SQL before any row is materialized: the total is a
        // COUNT over the filtered query, and only the page's rows are projected (one boolean per
        // taggable field, as EXISTS/negated-EXISTS subqueries in SQL - "non-blank" is a trimmed
        // non-empty value, matching the IsNullOrWhiteSpace predicates MissingTagService.Fields
        // runs against the row). A page never reads a Description-size blob or materializes a
        // collection for a book the page doesn't show.
        var total = await query.CountAsync();

        var items = await query
            .OrderBy(a => a.BookName)
            .ThenBy(a => a.Id)
            .Skip(skip)
            .Take(take)
            .Select(a => new MissingTagRow(
                a.Id,
                a.BookName,
                a.Authors.Select(p => p.Name).ToList(),
                a.Authors.Any(p => p.Name != null && p.Name.Trim() != ""),
                a.Narrators.Any(p => p.Name != null && p.Name.Trim() != ""),
                a.Genres.Any(),
                a.BookName == null || a.BookName.Trim() == "",
                a.Year == 0,
                a.Series == null || a.Series.Trim() == "",
                a.SeriesPart == null || a.SeriesPart.Trim() == "",
                a.Subtitle == null || a.Subtitle.Trim() == "",
                a.Description == null || a.Description.Trim() == "",
                a.Language == null || a.Language.Trim() == "",
                a.CoverFilePath == null || a.CoverFilePath.Trim() == "",
                a.Copyright == null || a.Copyright.Trim() == "",
                a.Publisher == null || a.Publisher.Trim() == "",
                a.Rating == null || a.Rating.Trim() == "",
                a.Asin == null || a.Asin.Trim() == "",
                a.Www == null || a.Www.Trim() == ""))
            .ToListAsync();

        return (items, total);
    }

    /// <summary>
    /// OR-combines an arbitrarily long set of <see cref="Expression{TDelegate}"/>s over the same
    /// entity type into one <c>WHERE a OR b OR c</c>. The predicates come from the service (each
    /// field's "is missing" test), so they cannot be handed to EF as a compiled delegate - it can
    /// only translate expression trees. Every predicate's parameter is rebound to one shared
    /// parameter so the subtree EF sees is a single lambda, not a call into a closure.
    /// </summary>
    private static Expression<Func<T, bool>> BuildOr<T>(IReadOnlyCollection<Expression<Func<T, bool>>> predicates)
    {
        var parameter = Expression.Parameter(typeof(T), "v");
        var body = predicates
            .Select(predicate => new ParameterRebinder(predicate.Parameters[0], parameter).Visit(predicate.Body))
            .Aggregate(Expression.OrElse);

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private sealed class ParameterRebinder : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ParameterRebinder(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? _to : base.VisitParameter(node);
    }

    public async Task<List<SeriesGroupingBook>> GetSeriesGroupingDataAsync()
    {
        var rows = await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series != null && a.Series != "")
            .Select(a => new
            {
                Series = a.Series!,
                a.SeriesPart,
                a.BookName,
                Authors = a.Authors.Select(p => p.Name).ToList(),
            })
            .ToListAsync();

        return rows
            .Select(r => new SeriesGroupingBook(r.Series, r.SeriesPart, r.BookName, r.Authors))
            .ToList();
    }

    /// <summary>
    /// <see cref="GetSeriesGroupingDataAsync()"/> bounded to one page of series values. The
    /// unbounded variant materialized every audiobook row in the library - Authors included - to
    /// build the overview the Series page has to render per series; paging the values first in
    /// <see cref="GetSeriesValuesPageAsync"/> and hydrating only that page's books keeps the read
    /// proportional to the rendered rows.
    /// </summary>
    public async Task<List<SeriesGroupingBook>> GetSeriesGroupingDataAsync(List<string> seriesValues)
    {
        if (seriesValues.Count == 0)
        {
            return new List<SeriesGroupingBook>();
        }

        var rows = await _db.Audiobooks
            .AsNoTracking()
            .Where(a => seriesValues.Contains(a.Series!))
            .Select(a => new
            {
                Series = a.Series!,
                a.SeriesPart,
                a.BookName,
                Authors = a.Authors.Select(p => p.Name).ToList(),
            })
            .ToListAsync();

        return rows
            .Select(r => new SeriesGroupingBook(r.Series, r.SeriesPart, r.BookName, r.Authors))
            .ToList();
    }

    public async Task<(List<string> Items, int Total)> GetSeriesValuesPageAsync(
        string? search, bool? matched, int skip, int take)
    {
        var folded = string.IsNullOrWhiteSpace(search) ? null : AccentFolding.FoldPlain(search!.Trim());
        var pattern = folded is null ? null : $"%{folded}%";

        var booksQuery = _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series != null && a.Series != "");

        if (pattern is not null)
        {
            // Mirrors the filter the overview page used to apply client-side: the series value
            // itself, or any author of the series' books, matched accent-insensitively. The
            // precomputed folded column keeps the LIKE off a per-row fold call, like every other
            // search in this repository. The matched condition is inlined (not factored into a
            // helper) because EF can only translate conditions written inline in the lambda.
            booksQuery = booksQuery.Where(a =>
                EF.Functions.Like(a.SeriesFolded, pattern) ||
                a.Authors.Any(p => EF.Functions.Like(p.NameFolded, pattern)));
        }

        if (matched is not null)
        {
            var wantMatched = matched == true;
            var matchedCatalog = _db.Series
                .AsNoTracking()
                .Where(s => s.MatchedSourceName != null && s.MatchedSourceName != ""
                    && s.MatchedSourceId != null && s.MatchedSourceId != "")
                .Select(s => s.Name);

            booksQuery = booksQuery.Where(a =>
                wantMatched ? matchedCatalog.Contains(a.Series!) : !matchedCatalog.Contains(a.Series!));
        }

        var fromBooks = booksQuery.Select(a => a.Series!).Distinct();

        var catalogQuery = _db.Series.AsNoTracking();
        if (pattern is not null)
        {
            catalogQuery = catalogQuery.Where(s => EF.Functions.Like(AccentFolding.Fold(s.Name), pattern));
        }

        if (matched is not null)
        {
            // Inline rather than factored into a helper: EF can only translate conditions
            // written inline in the lambda.
            catalogQuery = matched == true
                ? catalogQuery.Where(s => s.MatchedSourceName != null && s.MatchedSourceName != ""
                    && s.MatchedSourceId != null && s.MatchedSourceId != "")
                : catalogQuery.Where(s => s.MatchedSourceName == null || s.MatchedSourceName == ""
                    || s.MatchedSourceId == null || s.MatchedSourceId == "");
        }

        var fromCatalog = catalogQuery.Select(s => s.Name);

        // The two value spaces are one: a catalog row's name is exactly the free-text tag value it
        // documents, so a value present on both sides is the same series. UNION (not UNION ALL)
        // therefore removes the overlap, and the value itself is the total-order tiebreaker -
        // unique within the union, so ORDER BY the value alone keeps every page stable.
        var union = fromBooks.Union(fromCatalog);

        var total = await union.CountAsync();

        var items = await union
            .OrderBy(value => value)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return (items, total);
    }

    public async Task<(int Total, int Matched)> GetSeriesValueCountsAsync()
    {
        var fromBooks = _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series != null && a.Series != "")
            .Select(a => a.Series!)
            .Distinct();
        var fromCatalog = _db.Series.AsNoTracking().Select(s => s.Name);

        var union = fromBooks.Union(fromCatalog);

        var total = await union.CountAsync();
        // Inline matched condition (see GetSeriesValuesPageAsync for why).
        var matched = await _db.Series.AsNoTracking()
            .Where(s => s.MatchedSourceName != null && s.MatchedSourceName != ""
                && s.MatchedSourceId != null && s.MatchedSourceId != "")
            .CountAsync();

        return (total, matched);
    }

    /// <summary>
    /// All audiobooks reduced to the candidate-search fields, projected in SQL rather than loaded
    /// whole. Prefiltered by matching the title's tokens (accent-insensitive LIKE) against
    /// BookName so the service ranking only works against a bounded set, not the entire table.
    /// Bounded to <paramref name="limit"/> rows; final fuzzy ranking is performed in SeriesService.
    /// </summary>
    public async Task<List<SeriesCandidateBook>> GetSeriesCandidateDataAsync(string title, int limit)
    {
        // Patterns wrap each normalized token in %...% so LIKE is substring containment; the
        // escape character keeps a literal '%' or '_' that a token carries from acting as a
        // wildcard. Patterns are precomputed here - the LIKE shape has to reach EF as bound
        // parameters, a string.Format inside the query lambda cannot be translated to SQL.
        var patterns = GetNormalizedTokens(title).Select(t => $"%{t}%").ToList();

        // The matches run against the precomputed BookNameFolded column (kept in sync with
        // BookName by AccentFoldedColumnsInterceptor and backfilled by the
        // AddAccentFoldedSearchColumns migration) rather than wrapping BookName in
        // fold_accents() per row - exactly what SearchAsync/SearchSeriesAsync do, for the same
        // reason: a scalar-function call on the column costs a callback into managed code for
        // every row scanned, on every OR/AND term (#1303). BookNameFolded holds precisely
        // FoldPlain(BookName), the accent fold with no lowercasing, and the token patterns are
        // lowercase, so the swap is behavior-identical: SQLite's default LIKE is
        // case-insensitive over ASCII, which is the same case-folding the old query-time fold
        // relied on.

        // Two-phase prefilter. Phase one: books whose name contains EVERY title token - the
        // exact/near-exact matches the fuzzy ranker would score highest - get the cap's budget
        // first. That order is what stops a generic token ("the") that matches thousands of rows
        // from letting the alphabetical LIMIT starve the genuine match out of the set the ranker
        // ever sees (the rows now sort by coverage, not only by code point). Phase two keeps
        // rows sharing ANY single token - "Hero of Ages" must still surface for "The Hero of
        // Ages" even though it has no "the" - which is exactly the recall the former single-OR
        // query had, just ordered behind every full-coverage row.
        var allMatch = _db.Audiobooks.AsNoTracking();
        foreach (var pattern in patterns)
        {
            allMatch = allMatch.Where(a => EF.Functions.Like(a.BookNameFolded, pattern, LikeEscapeCharacter));
        }

        var allMatchRows = await QueryCandidateRows(allMatch, limit);
        if (allMatchRows.Count >= limit)
        {
            return allMatchRows;
        }

        // A single token collapses the all-match and broad predicates into the same LIKE, so
        // every matching row already came back above and the broad query - identical predicate
        // minus the ids just returned - is provably empty. Skip it rather than execute a wasted
        // query. The skip must stay keyed on the single-token shape: with two or more tokens
        // the broad (ANY) predicate is a strict superset of the all-match (ALL) one, and CAN
        // surface rows the cap truncated.
        if (patterns.Count == 1)
        {
            return allMatchRows;
        }

        var broad = _db.Audiobooks
            .AsNoTracking()
            .Where(a => patterns.Any(pattern => EF.Functions.Like(a.BookNameFolded, pattern, LikeEscapeCharacter)));

        var allMatchIds = allMatchRows.Select(r => r.Id).ToList();
        if (allMatchIds.Count > 0)
        {
            broad = broad.Where(a => !allMatchIds.Contains(a.Id));
        }

        var broadRows = await QueryCandidateRows(broad, limit - allMatchRows.Count);
        var merged = new List<SeriesCandidateBook>();
        merged.AddRange(allMatchRows);
        merged.AddRange(broadRows);
        return merged;
    }

    /// <summary>
    /// Applies the candidate projection and the total-order tiebreaker (<c>BookName, then Id</c>)
    /// to a prefiltered <see cref="Audiobook"/> query, capped at <paramref name="take"/> rows.
    /// </summary>
    private async Task<List<SeriesCandidateBook>> QueryCandidateRows(IQueryable<Audiobook> rows, int take)
    {
        var projected = await rows
            .OrderBy(a => a.BookName)
            .ThenBy(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.BookName,
                a.Series,
                a.SeriesPart,
                a.Year,
                Authors = a.Authors.Select(p => p.Name).ToList(),
            })
            .Take(take)
            .ToListAsync();

        return projected
            .Select(r => new SeriesCandidateBook(r.Id, r.BookName, r.Series, r.SeriesPart, r.Year, r.Authors))
            .ToList();
    }

    /// <summary>
    /// Normalize the input into LIKE-safe tokens using a local subset of NameNormalizer's behavior
    /// (accent fold, lowercase, strip punctuation, split, discard short tokens, deduplicate).
    /// This lives in the Database layer and does not import Services.
    ///
    /// A title carrying a literal '%' or '_' is kept as one whole escaped phrase instead of word
    /// tokens: those characters are LIKE metacharacters, and splitting around them would either
    /// drop them ("The % Book" would then search as "The Book" and match every 'The ? Book' in the
    /// library) or leave a bare wildcard that matches broadly. Kept whole and escaped, the phrase
    /// matches only the literal text.
    /// </summary>
    private static List<string> GetNormalizedTokens(string? title)
    {
        var folded = AccentFolding.FoldPlain(title) ?? string.Empty;
        var lowered = folded.ToLowerInvariant();

        if (lowered.Any(c => c == '%' || c == '_'))
        {
            return new List<string> { EscapeLikePattern(lowered) };
        }

        // Replace punctuation with spaces so "Dune:" tokenizes to "dune", matching library entries
        // that lack the trailing colon. Mirrors NameNormalizer's punctuation stripping without
        // importing the Services layer.
        var stripped = new string(lowered.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());

        var tokens = stripped
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 3)
            .Select(EscapeLikePattern)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (tokens.Count == 0)
        {
            tokens.Add(EscapeLikePattern(folded.ToLowerInvariant()));
        }

        return tokens;
    }

    /// <summary>
    /// Distinct series values only, for the detection of near-duplicate series values.
    /// </summary>
    public async Task<List<string>> GetSeriesNamesAsync()
    {
        // Sorted in memory rather than by SQL - see GetAuthorNamesAsync for why SQLite's BINARY
        // collation is the wrong order for a name list a human reads.
        var series = await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series != null && a.Series != "")
            .Select(a => a.Series!)
            .Distinct()
            .ToListAsync();

        series.Sort(StringComparer.InvariantCulture);
        return series;
    }

    public async Task<Dictionary<string, int>> GetSeriesBookCountsAsync(IReadOnlyCollection<string> seriesValues)
    {
        if (seriesValues.Count == 0)
        {
            return new Dictionary<string, int>();
        }

        var rows = await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series != null && seriesValues.Contains(a.Series))
            .GroupBy(a => a.Series!)
            .Select(g => new { Series = g.Key, Count = g.Count() })
            .ToListAsync();

        return rows.ToDictionary(r => r.Series, r => r.Count, StringComparer.Ordinal);
    }

    public async Task<List<Audiobook>> GetBooksByAuthorNamesAsync(IEnumerable<string> authorNames)
    {
        var names = authorNames.ToList();
        return await _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres.OrderBy(g => g.Name))
            .AsSplitQuery()
            .Where(a => a.Authors.Any(p => names.Contains(p.Name)))
            .ToListAsync();
    }

    public async Task<List<Audiobook>> GetBooksByPersonNamesAsync(IEnumerable<string> personNames)
    {
        var names = personNames.ToList();
        return await _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres.OrderBy(g => g.Name))
            .AsSplitQuery()
            .Where(a => a.Authors.Any(p => names.Contains(p.Name))
                        || a.Narrators.Any(p => names.Contains(p.Name)))
            .ToListAsync();
    }

    public async Task<List<Audiobook>> GetBooksBySeriesValuesAsync(IEnumerable<string> seriesValues)
    {
        var values = seriesValues.ToList();
        return await _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres.OrderBy(g => g.Name))
            .AsSplitQuery()
            .Where(a => a.Series != null && values.Contains(a.Series))
            .ToListAsync();
    }

    public async Task UpdateFilePathAsync(long id, string newFullPath, string newFileName)
    {
        var audiobook = await _db.Audiobooks.FindAsync(id);
        if (audiobook != null)
        {
            audiobook.FileInfoFullPath = newFullPath;
            audiobook.FileInfoFileName = newFileName;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Books with no language recorded, as (id, file path) pairs for the backfill to read the
    /// embedded tag from. Ordered by id so a run is reproducible and its progress monotonic.
    /// </summary>
    public async Task<List<AudiobookLanguageRef>> GetBooksMissingLanguageAsync()
    {
        return await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Language == null || a.Language == "")
            .OrderBy(a => a.Id)
            .Select(a => new AudiobookLanguageRef(a.Id, a.FileInfoFullPath))
            .ToListAsync();
    }

    public async Task<List<MetadataRefreshEligibleBook>> GetBooksEligibleForMetadataRefreshAsync(DateTime? staleBeforeUtc)
    {
        return await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Www != null && a.Www != "" &&
                (staleBeforeUtc == null || a.LastMetadataRefreshedAt == null || a.LastMetadataRefreshedAt < staleBeforeUtc))
            .OrderBy(a => a.LastMetadataRefreshedAt)
            .ThenBy(a => a.Id)
            .Select(a => new MetadataRefreshEligibleBook(a.Id, a.Www, a.LastMetadataRefreshedAt))
            .ToListAsync();
    }

    /// <summary>
    /// Sets just the language column.
    ///
    /// A direct database write is deliberate and safe here, unlike for the fields the binding
    /// invariant in CLAUDE.md covers: Language plays no part in
    /// <c>GenerateRelativeAudiobookPath</c>, so nothing needs relocating, and the only caller
    /// (the backfill) is copying the value *out of* the book's own m4b tag - it cannot desync a
    /// file from its record, because it is reading what the file already says.
    /// </summary>
    public async Task UpdateLanguageAsync(long id, string? language)
    {
        var audiobook = await _db.Audiobooks.FindAsync(id);
        if (audiobook != null)
        {
            audiobook.Language = language;
            await _db.SaveChangesAsync();
        }
    }

    public async Task UpdateCoverFilePathAsync(long id, string? coverFilePath)
    {
        var audiobook = await _db.Audiobooks.FindAsync(id);
        if (audiobook != null)
        {
            audiobook.CoverFilePath = coverFilePath;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Bookkeeping-only column write. A direct write is deliberate and safe, exactly as for
    /// <see cref="UpdateLanguageAsync"/>: the timestamp plays no part in
    /// <c>GenerateRelativeAudiobookPath</c> and is not an m4b tag, so nothing can desync a file
    /// from its record.
    /// </summary>
    public async Task UpdateLastMetadataRefreshedAtAsync(long id, DateTime? whenUtc)
    {
        var audiobook = await _db.Audiobooks.FindAsync(id);
        if (audiobook != null)
        {
            audiobook.LastMetadataRefreshedAt = whenUtc;
            await _db.SaveChangesAsync();
        }
    }

    public async Task DeleteAudiobookAsync(long id)
    {
        var audiobook = await _db.Audiobooks.FindAsync(id);
        if (audiobook != null)
        {
            _db.Audiobooks.Remove(audiobook);
            await _db.SaveChangesAsync();
        }
    }

    public async Task UpdateAudiobookAsync(Audiobook audiobook)
    {
        // Update() only for a detached entity. Callers normally hand back the graph they loaded
        // from GetByIdWithIncludesAsync, which is already tracked and whose changes the change
        // tracker has already worked out - calling Update() on that forces every reachable
        // entity to Modified, so saving one book also emitted a pointless UPDATE for each of its
        // authors, narrators and genres (shared persons/genres rows, rewritten for nothing, once
        // per book in a several-hundred-book alignment run).
        if (_db.Entry(audiobook).State == EntityState.Detached)
        {
            _db.Audiobooks.Update(audiobook);
        }

        await _db.SaveChangesAsync();
    }
}
