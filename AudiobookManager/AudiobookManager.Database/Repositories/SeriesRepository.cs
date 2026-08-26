using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class SeriesRepository : ISeriesRepository
{
    private readonly DatabaseContext _db;

    public SeriesRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<List<Series>> GetAllWithExpectedBooksAsync()
    {
        return await _db.Series
            .Include(s => s.ExpectedBooks)
            .AsSplitQuery()
            .ToListAsync();
    }

    public async Task<Series?> GetByIdWithExpectedBooksAsync(long id)
    {
        return await _db.Series
            .Include(s => s.ExpectedBooks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<Series?> GetByNameWithExpectedBooksAsync(string name)
    {
        return await _db.Series
            .Include(s => s.ExpectedBooks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Name == name);
    }

    /// <summary>
    /// Inserts the series if no row with the same <see cref="Series.Name"/> exists,
    /// otherwise updates the match metadata on the existing row.
    /// </summary>
    public async Task<Series> UpsertSeriesAsync(Series series)
    {
        var existing = await _db.Series.FirstOrDefaultAsync(s => s.Name == series.Name);

        if (existing is null)
        {
            _db.Series.Add(series);
            await _db.SaveChangesAsync();
            return series;
        }

        existing.MatchedSourceName = series.MatchedSourceName;
        existing.MatchedSourceId = series.MatchedSourceId;
        existing.MatchedSourceUrl = series.MatchedSourceUrl;
        existing.MatchedSeriesName = series.MatchedSeriesName;
        existing.MatchConfidence = series.MatchConfidence;
        existing.LastRefreshedAt = series.LastRefreshedAt;

        await _db.SaveChangesAsync();
        return existing;
    }

    public async Task ReplaceExpectedBooksAsync(long seriesId, List<SeriesExpectedBook> expectedBooks)
    {
        var existing = await _db.SeriesExpectedBooks
            .Where(b => b.SeriesId == seriesId)
            .ToListAsync();

        _db.SeriesExpectedBooks.RemoveRange(existing);

        foreach (var book in expectedBooks)
        {
            book.Id = 0;
            book.SeriesId = seriesId;
            _db.SeriesExpectedBooks.Add(book);
        }

        await _db.SaveChangesAsync();
    }

    public async Task<SeriesExpectedBook?> GetExpectedBookAsync(long id)
    {
        return await _db.SeriesExpectedBooks.FindAsync(id);
    }

    public async Task SetExpectedBookIgnoredAsync(long id, bool ignored)
    {
        var book = await _db.SeriesExpectedBooks.FindAsync(id);
        if (book is null)
        {
            throw new KeyNotFoundException($"Expected book {id} not found");
        }

        book.IsIgnored = ignored;
        await _db.SaveChangesAsync();
    }
}
