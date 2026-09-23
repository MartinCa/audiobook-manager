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
            existing.ChangedFieldsJson = refresh.ChangedFieldsJson;
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
            .OrderByDescending(p => p.FetchedAt)
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

    public Task<List<long>> GetAudiobookIdsMissingChangedFieldsAsync() =>
        _db.PendingMetadataRefreshes
            .AsNoTracking()
            .Where(p => p.ChangedFieldsJson == null)
            .Select(p => p.AudiobookId)
            .ToListAsync();

    public async Task SetChangedFieldsJsonAsync(long audiobookId, string changedFieldsJson)
    {
        await _db.PendingMetadataRefreshes
            .Where(p => p.AudiobookId == audiobookId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ChangedFieldsJson, changedFieldsJson));
    }

    public Task<List<PendingRefreshFieldsRow>> GetAllChangedFieldsAsync() =>
        _db.PendingMetadataRefreshes
            .AsNoTracking()
            .Select(p => new PendingRefreshFieldsRow(p.AudiobookId, p.FetchedAt, p.ChangedFieldsJson))
            .ToListAsync();

    public Task<List<PendingMetadataRefresh>> GetByAudiobookIdsAsync(IReadOnlyCollection<long> audiobookIds)
    {
        if (audiobookIds.Count == 0)
        {
            return Task.FromResult(new List<PendingMetadataRefresh>());
        }

        return _db.PendingMetadataRefreshes
            .AsNoTracking()
            .Where(p => audiobookIds.Contains(p.AudiobookId))
            .ToListAsync();
    }

    public async Task<List<PendingMetadataRefresh>> GetByAudiobookIdsWithAudiobookAsync(IReadOnlyCollection<long> audiobookIds)
    {
        if (audiobookIds.Count == 0)
        {
            return new List<PendingMetadataRefresh>();
        }

        return await _db.PendingMetadataRefreshes
            .AsNoTracking()
            .Include(p => p.Audiobook)
                .ThenInclude(a => a.Authors)
            .AsSplitQuery()
            .Where(p => audiobookIds.Contains(p.AudiobookId))
            .ToListAsync();
    }

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