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
            ApplyRefresh(existing, release);
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

            ApplyRefresh(winner, release);
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Applies a freshly-polled release onto an already-known row. Title/date/source URL are
    /// always overwritten - Hardcover (and every other source) routinely ships an announced book
    /// under a placeholder title ("Untitled Mistborn novel") or a provisional date, and corrects
    /// it once real details are available, so the stored row must track that correction on the
    /// next poll rather than freezing it at first discovery. Title is guaranteed non-empty and
    /// SourceUrl is always derivable from the book's id/slug (see
    /// <c>HardcoverScraper.ParseUpcomingBook</c>), so overwriting them unconditionally is safe.
    /// <see cref="UpcomingRelease.ImageUrl"/> is the one refreshed field that is legitimately
    /// nullable per poll (a transiently-missing <c>cached_image</c>), so it only overwrites when
    /// the new poll actually has one - a poll with no image must not wipe a previously stored
    /// one. PersonId/SeriesId/SeriesPosition are filled in only when still null, and never
    /// cleared or replaced once set, so a book discovered through both a followed author and a
    /// followed series keeps both links even if a later poll (of just one side) doesn't itself
    /// carry the other. <see cref="UpcomingRelease.DiscoveredAt"/> is likewise untouched - it is
    /// when this release was first seen, not when it was last refreshed.
    /// </summary>
    private static void ApplyRefresh(UpcomingRelease existing, UpcomingRelease polled)
    {
        existing.Title = polled.Title;
        existing.ReleaseDate = polled.ReleaseDate;
        existing.SourceUrl = polled.SourceUrl;
        existing.ImageUrl = polled.ImageUrl ?? existing.ImageUrl;
        existing.PersonId ??= polled.PersonId;
        existing.SeriesId ??= polled.SeriesId;
        existing.SeriesPosition ??= polled.SeriesPosition;
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

    public async Task<List<UpcomingRelease>> GetAllAsync(long? personId, long? seriesId)
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

        return await query
            .Include(r => r.Person)
            .Include(r => r.Series)
            .ToListAsync();
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var deleted = await _db.UpcomingReleases.Where(r => r.Id == id).ExecuteDeleteAsync();
        return deleted > 0;
    }
}
