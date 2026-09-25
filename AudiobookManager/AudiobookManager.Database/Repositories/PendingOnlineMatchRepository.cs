using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class PendingOnlineMatchRepository : IPendingOnlineMatchRepository
{
    private readonly DatabaseContext _db;

    public PendingOnlineMatchRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<PendingOnlineMatch> UpsertAsync(PendingOnlineMatch match)
    {
        var existing = await _db.PendingOnlineMatches
            .FirstOrDefaultAsync(m => m.AudiobookId == match.AudiobookId);

        if (existing is null)
        {
            _db.PendingOnlineMatches.Add(match);
        }
        else
        {
            existing.SearchedAt = match.SearchedAt;
            existing.Status = match.Status;
            existing.SourceNamesJson = match.SourceNamesJson;
            existing.ResultsJson = match.ResultsJson;
        }

        await _db.SaveChangesAsync();
        return existing ?? match;
    }

    public Task<PendingOnlineMatch?> GetByAudiobookIdAsync(long audiobookId) =>
        _db.PendingOnlineMatches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.AudiobookId == audiobookId);

    public async Task<bool> DeleteByAudiobookIdAsync(long audiobookId)
    {
        var deleted = await _db.PendingOnlineMatches
            .Where(m => m.AudiobookId == audiobookId)
            .ExecuteDeleteAsync();
        return deleted > 0;
    }

    public async Task<bool> SetStatusAsync(long audiobookId, PendingOnlineMatchStatus status)
    {
        var updated = await _db.PendingOnlineMatches
            .Where(m => m.AudiobookId == audiobookId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, status));
        return updated > 0;
    }

    public async Task<(List<PendingOnlineMatch> Items, int TotalCount)> GetPageWithAudiobookAsync(
        PendingOnlineMatchStatus status, int skip, int take)
    {
        var all = _db.PendingOnlineMatches.AsNoTracking().Where(m => m.Status == status);

        var totalCount = await all.CountAsync();

        var items = await all
            .Include(m => m.Audiobook)
                .ThenInclude(a => a.Authors)
            .AsSplitQuery()
            .OrderByDescending(m => m.SearchedAt)
            .ThenBy(m => m.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return (items, totalCount);
    }
}
