using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class SeriesFollowRepository : ISeriesFollowRepository
{
    private readonly DatabaseContext _db;

    public SeriesFollowRepository(DatabaseContext db)
    {
        _db = db;
    }

    public Task<bool> IsFollowedAsync(long seriesId) =>
        _db.SeriesFollows.AsNoTracking().AnyAsync(f => f.SeriesId == seriesId);

    public async Task FollowAsync(long seriesId)
    {
        var exists = await _db.SeriesFollows.AnyAsync(f => f.SeriesId == seriesId);
        if (exists)
        {
            return;
        }

        _db.SeriesFollows.Add(new SeriesFollow { SeriesId = seriesId, CreatedAt = DateTime.UtcNow });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // Lost a race against a concurrent follow of the same series - the row is already
            // there, which is exactly what this call asked for.
        }
    }

    public async Task UnfollowAsync(long seriesId)
    {
        await _db.SeriesFollows
            .Where(f => f.SeriesId == seriesId)
            .ExecuteDeleteAsync();
    }

    public async Task<List<Series>> GetFollowedMatchedSeriesAsync()
    {
        return await _db.SeriesFollows
            .AsNoTracking()
            .Where(f => f.Series.MatchedSourceId != null && f.Series.MatchedSourceId != ""
                && f.Series.MatchedSourceName != null && f.Series.MatchedSourceName != "")
            .Select(f => f.Series)
            .ToListAsync();
    }
}
