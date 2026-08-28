using AudiobookManager.Database.Models;
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

    public async Task<Audiobook?> GetByFullPathAsync(string fullPath)
    {
        return await _db.Audiobooks.FirstOrDefaultAsync(a => a.FileInfoFullPath == fullPath);
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
        var pattern = $"%{query}%";

        var dbQuery = _db.Audiobooks
            .AsNoTracking()
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .AsSplitQuery()
            .Where(a =>
                (a.BookName != null && EF.Functions.Like(a.BookName, pattern)) ||
                (a.Subtitle != null && EF.Functions.Like(a.Subtitle, pattern)) ||
                (a.Description != null && EF.Functions.Like(a.Description, pattern)) ||
                (a.Series != null && EF.Functions.Like(a.Series, pattern)) ||
                a.Authors.Any(p => EF.Functions.Like(p.Name, pattern))
            )
            .OrderBy(a => a.BookName).ThenBy(a => a.Id);

        var total = await dbQuery.CountAsync();
        var items = await dbQuery.Skip(offset).Take(limit).ToListAsync();
        return (items, total);
    }

    public async Task<List<(string Series, int BookCount)>> SearchSeriesAsync(string query, int limit)
    {
        var pattern = $"%{query}%";

        var rows = await _db.Audiobooks
            .Where(a => a.Series != null && a.Series != "" && EF.Functions.Like(a.Series, pattern))
            .GroupBy(a => a.Series!)
            .Select(g => new { Series = g.Key, BookCount = g.Count() })
            .OrderBy(g => g.Series)
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
            .OrderBy(r => r.Series, StringComparer.Ordinal)
            .Select(r => (r.Series, r.BookCount))
            .ToList();
    }

    /// <summary>The author's books that belong to no series - the only ones rendered in full.</summary>
    public async Task<List<Audiobook>> GetStandaloneBooksByAuthorAsync(long authorId)
    {
        return await _db.Audiobooks
            .AsNoTracking()
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .AsSplitQuery()
            .Where(a => (a.Series == null || a.Series == "") && a.Authors.Any(p => p.Id == authorId))
            .OrderBy(a => a.BookName).ThenBy(a => a.Id)
            .ToListAsync();
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
        return await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Series != null && a.Series != "")
            .Select(a => a.Series!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();
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
        _db.Audiobooks.Update(audiobook);
        await _db.SaveChangesAsync();
    }
}
