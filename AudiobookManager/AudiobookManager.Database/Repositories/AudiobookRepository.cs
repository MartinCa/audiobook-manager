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

    public async Task<(List<DirtyUrlRow> Items, int Total)> GetDirtyUrlPageAsync(int limit, int offset)
    {
        var dirty = _db.Audiobooks
            .AsNoTracking()
            // Dirty is what BookUrlCleaner would change: a query string or fragment after the
            // path. Expressing that in SQL keeps the page and its total exact, so the pager sizes
            // itself from the number of *rows actually rendered*, not from a superset that leaves
            // trailing pages empty. Blank, not just null: an empty string is "no URL" too.
            .Where(a => a.Www != null && a.Www != "" && (a.Www.Contains('?') || a.Www.Contains('#')));

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

    public async Task<int> CountDirtyUrlsAsync()
    {
        return await _db.Audiobooks
            .AsNoTracking()
            // Same dirty predicate as the page query - the header count and the pager total are
            // the same number by construction.
            .Where(a => a.Www != null && a.Www != "" && (a.Www.Contains('?') || a.Www.Contains('#')))
            .CountAsync();
    }

    public async Task<(List<Audiobook> Items, int Total)> GetAllAsync(int limit, int offset)
    {
        var query = _db.Audiobooks
            .AsNoTracking()
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
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

    public async Task<(List<Audiobook> Items, int Total)> SearchAsync(string query, int limit, int offset)
    {
        // Fold both sides so an unaccented query (e.g. "Rene") still matches an accented value
        // ("René") - SQLite's default BINARY collation, which LIKE uses here, never does that.
        var folded = AccentFolding.FoldPlain(query);
        var pattern = $"%{folded}%";
        var prefixPattern = $"{folded}%";

        var dbQuery = _db.Audiobooks
            .AsNoTracking()
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .AsSplitQuery()
            // fold_accents is an application-defined scalar function, so every term here costs a
            // callback into managed code per row. SQLite evaluates an OR chain left to right and
            // stops at the first true, so the cheap, most-selective columns come first and
            // Description - by far the largest, and the least likely to be what the user meant -
            // comes last, where most rows never reach it.
            .Where(a =>
                (a.BookName != null && EF.Functions.Like(AccentFolding.Fold(a.BookName), pattern)) ||
                (a.Subtitle != null && EF.Functions.Like(AccentFolding.Fold(a.Subtitle), pattern)) ||
                (a.Series != null && EF.Functions.Like(AccentFolding.Fold(a.Series), pattern)) ||
                a.Authors.Any(p => EF.Functions.Like(AccentFolding.Fold(p.Name), pattern)) ||
                (a.Description != null && EF.Functions.Like(AccentFolding.Fold(a.Description), pattern))
            )
            // Rank in SQL, before Skip/Take. Ordering by title alone and ranking the survivors
            // in the controller meant a limit-5 type-ahead kept the five alphabetically-first
            // matches and re-ranked those - so searching "harry" in a library holding "Alex
            // Rider" ... "Harry Potter" never surfaced the one title that actually starts with
            // it. ThenBy(Id) keeps the order total, which a paged split query requires.
            .OrderByDescending(a =>
                (a.BookName != null && EF.Functions.Like(AccentFolding.Fold(a.BookName), prefixPattern)) ||
                a.Authors.Any(p => EF.Functions.Like(AccentFolding.Fold(p.Name), prefixPattern)))
            .ThenBy(a => a.BookName)
            .ThenBy(a => a.Id);

        var total = await dbQuery.CountAsync();
        var items = await dbQuery.Skip(offset).Take(limit).ToListAsync();
        return (items, total);
    }

    public async Task<List<(string Series, int BookCount)>> SearchSeriesAsync(string query, int limit)
    {
        var folded = AccentFolding.FoldPlain(query);
        var pattern = $"%{folded}%";
        var prefixPattern = $"{folded}%";

        var rows = await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series != null && a.Series != "" && EF.Functions.Like(AccentFolding.Fold(a.Series), pattern))
            .GroupBy(a => a.Series!)
            .Select(g => new { Series = g.Key, BookCount = g.Count() })
            // Rank before the limit, not after it - see SearchAsync for what ranking the
            // survivors of an alphabetical Take costs.
            .OrderByDescending(g => EF.Functions.Like(AccentFolding.Fold(g.Series), prefixPattern))
            .ThenBy(g => g.Series)
            .Take(limit)
            .ToListAsync();

        return rows.Select(r => (r.Series, r.BookCount)).ToList();
    }

    public async Task<List<Audiobook>> GetBooksBySeriesAsync(string seriesName, long? authorId)
    {
        var query = _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .AsSplitQuery()
            .Where(a => a.Series == seriesName);

        if (authorId.HasValue)
        {
            query = query.Where(a => a.Authors.Any(p => p.Id == authorId.Value));
        }

        return await query.OrderBy(a => a.SeriesPart).ThenBy(a => a.Id).ToListAsync();
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
    /// Per-series book counts for one author, aggregated in SQL. The author detail view only
    /// renders a name and a count for each series, so the books themselves are never loaded.
    /// </summary>
    public async Task<List<(string Series, int BookCount)>> GetSeriesCountsByAuthorAsync(long authorId)
    {
        var rows = await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series != null && a.Series != "" && a.Authors.Any(p => p.Id == authorId))
            .GroupBy(a => a.Series!)
            .Select(g => new { Series = g.Key, BookCount = g.Count() })
            .ToListAsync();

        return rows
            .OrderBy(r => r.Series, StringComparer.InvariantCulture)
            .Select(r => (r.Series, r.BookCount))
            .ToList();
    }

    /// <summary>The author's books that belong to no series - the only ones rendered in full.</summary>
    public async Task<List<Audiobook>> GetStandaloneBooksByAuthorAsync(long authorId)
    {
        var books = await _db.Audiobooks
            .AsNoTracking()
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .AsSplitQuery()
            .Where(a => (a.Series == null || a.Series == "") && a.Authors.Any(p => p.Id == authorId))
            .ToListAsync();

        // Title order for a human, so sorted in memory rather than by SQL's BINARY collation.
        return books
            .OrderBy(a => a.BookName, StringComparer.InvariantCulture)
            .ThenBy(a => a.Id)
            .ToList();
    }

    public async Task<Audiobook?> GetByIdWithIncludesAsync(long id)
    {
        return await _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<List<Audiobook>> GetAllWithIncludesAsync()
    {
        return await _db.Audiobooks
            .AsNoTracking()
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .AsSplitQuery()
            .OrderBy(a => a.BookName).ThenBy(a => a.Id)
            .ToListAsync();
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
    /// Distinct series values only. The autocomplete name list needs no book rows behind them,
    /// so it must not go through <see cref="GetDistinctSeriesAsync"/>.
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

    public async Task<Dictionary<string, List<(long Id, string BookName)>>> GetDistinctSeriesAsync()
    {
        var rows = await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series != null && a.Series != "")
            .Select(a => new { a.Id, a.BookName, Series = a.Series! })
            .ToListAsync();

        return rows
            .GroupBy(r => r.Series)
            .ToDictionary(g => g.Key, g => g.Select(r => (r.Id, r.BookName)).ToList());
    }

    public async Task<List<Audiobook>> GetBooksByAuthorNamesAsync(IEnumerable<string> authorNames)
    {
        var names = authorNames.ToList();
        return await _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .AsSplitQuery()
            .Where(a => a.Authors.Any(p => names.Contains(p.Name)))
            .ToListAsync();
    }

    public async Task<List<Audiobook>> GetBooksBySeriesValuesAsync(IEnumerable<string> seriesValues)
    {
        var values = seriesValues.ToList();
        return await _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
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
