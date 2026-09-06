using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class PendingMetadataRefreshRepository : IPendingMetadataRefreshRepository
{
    private readonly DatabaseContext _db;

    public PendingMetadataRefreshRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<PendingMetadataRefresh> UpsertAsync(PendingMetadataRefresh refresh)
    {
        var existing = await _db.PendingMetadataRefreshes
            .FirstOrDefaultAsync(p => p.AudiobookId == refresh.AudiobookId);

        if (existing is null)
        {
            _db.PendingMetadataRefreshes.Add(refresh);
        }
        else
        {
            existing.FetchedAt = refresh.FetchedAt;
            existing.SourceName = refresh.SourceName;
            existing.SourceUrl = refresh.SourceUrl;
            existing.PayloadJson = refresh.PayloadJson;
        }

        await _db.SaveChangesAsync();
        return existing ?? refresh;
    }

    public Task<PendingMetadataRefresh?> GetByAudiobookIdAsync(long audiobookId) =>
        _db.PendingMetadataRefreshes
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AudiobookId == audiobookId);

    public async Task<bool> DeleteByAudiobookIdAsync(long audiobookId)
    {
        var deleted = await _db.PendingMetadataRefreshes
            .Where(p => p.AudiobookId == audiobookId)
            .ExecuteDeleteAsync();
        return deleted > 0;
    }

    public async Task<(List<PendingMetadataRefresh> Items, int TotalCount)> GetPageWithAudiobookAsync(int skip, int take)
    {
        var all = _db.PendingMetadataRefreshes.AsNoTracking();

        var totalCount = await all.CountAsync();

        var items = await all
            .Include(p => p.Audiobook)
                .ThenInclude(a => a.Authors)
            .AsSplitQuery()
            .OrderBy(p => p.AudiobookId)
            .ThenBy(p => p.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return (items, totalCount);
    }

    public Task<List<long>> GetPendingAudiobookIdsAsync() =>
        _db.PendingMetadataRefreshes
            .AsNoTracking()
            .Select(p => p.AudiobookId)
            .ToListAsync();

    public async Task<int> DeleteAllByAudiobookIdsAsync(IReadOnlyCollection<long> audiobookIds)
    {
        if (audiobookIds.Count == 0)
        {
            return 0;
        }

        var deleted = await _db.PendingMetadataRefreshes
            .Where(p => audiobookIds.Contains(p.AudiobookId))
            .ExecuteDeleteAsync();
        return deleted;
    }
}