using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class SeriesConsistencyIssueRepository : ISeriesConsistencyIssueRepository
{
    private readonly DatabaseContext _db;

    public SeriesConsistencyIssueRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<(List<SeriesConsistencyIssue> Items, int TotalCount)> GetPageWithSeriesAsync(int skip, int take)
    {
        var matching = _db.SeriesConsistencyIssues.AsNoTracking();

        var totalCount = await matching.CountAsync();

        var items = await matching
            .Include(i => i.Series)
            .OrderByDescending(i => i.DetectedAt)
            .ThenBy(i => i.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task UpsertFailureAsync(long seriesId, string errorMessage)
    {
        var existing = await _db.SeriesConsistencyIssues
            .FirstOrDefaultAsync(i => i.SeriesId == seriesId);

        if (existing is null)
        {
            _db.Add(new SeriesConsistencyIssue
            {
                SeriesId = seriesId,
                ErrorMessage = errorMessage,
                DetectedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.ErrorMessage = errorMessage;
            existing.DetectedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteBySeriesIdAsync(long seriesId)
    {
        await _db.SeriesConsistencyIssues
            .Where(i => i.SeriesId == seriesId)
            .ExecuteDeleteAsync();

        foreach (var entry in _db.ChangeTracker.Entries<SeriesConsistencyIssue>()
                     .Where(e => e.Entity.SeriesId == seriesId)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
