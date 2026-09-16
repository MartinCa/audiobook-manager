using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class PendingSeriesRefreshRepository : IPendingSeriesRefreshRepository
{
    private readonly DatabaseContext _db;

    public PendingSeriesRefreshRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<PendingSeriesRefresh?> GetBySeriesNameAsync(string seriesName)
    {
        return await _db.PendingSeriesRefreshes
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.SeriesName == seriesName);
    }

    public async Task<PendingSeriesRefresh> UpsertAsync(PendingSeriesRefresh pending)
    {
        var existing = await _db.PendingSeriesRefreshes
            .FirstOrDefaultAsync(p => p.SeriesName == pending.SeriesName);

        if (existing is not null)
        {
            Apply(existing, pending);
            await _db.SaveChangesAsync();
            return existing;
        }

        _db.PendingSeriesRefreshes.Add(pending);

        try
        {
            await _db.SaveChangesAsync();
            return pending;
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // series_name is unique and this reads before it inserts, across an await on a
            // request-scoped context - two callers can both find the row missing and both insert
            // it. Every refresh/apply writer currently serializes on the controller's refresh
            // gate, but the repository must not depend on that; adopt the winner's row the same
            // way the other read-then-insert repositories do.
            _db.Entry(pending).State = EntityState.Detached;

            var winner = await _db.PendingSeriesRefreshes
                .FirstOrDefaultAsync(p => p.SeriesName == pending.SeriesName);
            if (winner is null)
            {
                // Some other uniqueness constraint failed - not the race this handler is for.
                throw;
            }

            Apply(winner, pending);
            await _db.SaveChangesAsync();
            return winner;
        }
    }

    private static void Apply(PendingSeriesRefresh target, PendingSeriesRefresh source)
    {
        target.FetchedAt = source.FetchedAt;
        target.SourceName = source.SourceName;
        target.SourceUrl = source.SourceUrl;
        target.PayloadJson = source.PayloadJson;
    }

    public async Task<bool> DeleteBySeriesNameAsync(string seriesName)
    {
        var deleted = await _db.PendingSeriesRefreshes
            .Where(p => p.SeriesName == seriesName)
            .ExecuteDeleteAsync();
        return deleted > 0;
    }

    public async Task<(List<PendingSeriesRefresh> Items, int TotalCount)> GetPageAsync(int skip, int take)
    {
        var all = _db.PendingSeriesRefreshes.AsNoTracking();

        var totalCount = await all.CountAsync();

        var items = await all
            .OrderByDescending(p => p.FetchedAt)
            .ThenBy(p => p.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return (items, totalCount);
    }

    public Task<int> CountAsync() =>
        _db.PendingSeriesRefreshes.AsNoTracking().CountAsync();
}