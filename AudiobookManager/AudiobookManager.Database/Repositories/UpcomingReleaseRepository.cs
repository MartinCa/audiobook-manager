using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class UpcomingReleaseRepository : IUpcomingReleaseRepository
{
    private readonly DatabaseContext _db;

    public UpcomingReleaseRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task UpsertAsync(UpcomingRelease release)
    {
        var existing = await _db.UpcomingReleases.FirstOrDefaultAsync(
            r => r.SourceName == release.SourceName && r.SourceBookId == release.SourceBookId);

        if (existing is not null)
        {
            existing.PersonId ??= release.PersonId;
            existing.SeriesId ??= release.SeriesId;
            existing.SeriesPosition ??= release.SeriesPosition;
            await _db.SaveChangesAsync();
            return;
        }

        _db.UpcomingReleases.Add(release);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // Lost a race against a concurrent poll discovering the same book (an author and a
            // series it belongs to can both be followed and polled around the same time) -
            // adopt the winner's row and apply the same fill-in this call would have done.
            _db.Entry(release).State = EntityState.Detached;

            var winner = await _db.UpcomingReleases.FirstOrDefaultAsync(
                r => r.SourceName == release.SourceName && r.SourceBookId == release.SourceBookId);
            if (winner is null)
            {
                throw;
            }

            winner.PersonId ??= release.PersonId;
            winner.SeriesId ??= release.SeriesId;
            winner.SeriesPosition ??= release.SeriesPosition;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<(List<UpcomingRelease> Items, int Total)> GetPagedAsync(
        long? personId, long? seriesId, int limit, int offset)
    {
        var query = _db.UpcomingReleases.AsNoTracking();

        if (personId is not null)
        {
            query = query.Where(r => r.PersonId == personId);
        }

        if (seriesId is not null)
        {
            query = query.Where(r => r.SeriesId == seriesId);
        }

        var total = await query.CountAsync();

        // Soonest release first, with discovery time and id as tiebreakers so the order is
        // total and stable across pages.
        var items = await query
            .Include(r => r.Person)
            .Include(r => r.Series)
            .OrderBy(r => r.ReleaseDate)
            .ThenBy(r => r.DiscoveredAt)
            .ThenBy(r => r.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return (items, total);
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var deleted = await _db.UpcomingReleases.Where(r => r.Id == id).ExecuteDeleteAsync();
        return deleted > 0;
    }
}
