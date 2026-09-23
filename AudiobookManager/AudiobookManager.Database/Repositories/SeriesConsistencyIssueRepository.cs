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

        if (existing is not null)
        {
            Apply(existing, errorMessage);
            await _db.SaveChangesAsync();
            return;
        }

        var issue = new SeriesConsistencyIssue
        {
            SeriesId = seriesId,
            ErrorMessage = errorMessage,
            DetectedAt = DateTime.UtcNow,
        };
        _db.Add(issue);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // series_id is unique and this reads before it inserts, across an await on a
            // request-scoped context - a single refresh and the fire-and-forget bulk sweep can
            // both fail the same series concurrently, both miss this read, and both insert.
            // Adopt the winner's row the same way PendingSeriesRefreshRepository.UpsertAsync does.
            _db.Entry(issue).State = EntityState.Detached;

            var winner = await _db.SeriesConsistencyIssues
                .FirstOrDefaultAsync(i => i.SeriesId == seriesId);
            if (winner is null)
            {
                // Some other uniqueness constraint failed - not the race this handler is for.
                throw;
            }

            Apply(winner, errorMessage);
            await _db.SaveChangesAsync();
        }
    }

    private static void Apply(SeriesConsistencyIssue target, string errorMessage)
    {
        target.ErrorMessage = errorMessage;
        target.DetectedAt = DateTime.UtcNow;
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
