using AudiobookManager.Database.Models;
using AudiobookManager.Database.Search;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class DiscoveredAudiobookRepository : IDiscoveredAudiobookRepository
{
    private readonly DatabaseContext _db;

    public DiscoveredAudiobookRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task InsertAsync(DiscoveredAudiobook discovered)
    {
        _db.Add(discovered);
        await _db.SaveChangesAsync();
    }

    public async Task<List<DiscoveredAudiobook>> GetAllAsync()
    {
        return await _db.DiscoveredAudiobooks.ToListAsync();
    }

    public async Task<(List<DiscoveredAudiobook> Items, int Total)> GetPaginatedAsync(int limit, int offset, string? search = null)
    {
        var query = _db.DiscoveredAudiobooks.AsNoTracking().OrderBy(d => d.FileInfoFullPath).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{AccentFolding.FoldPlain(search)}%";
            query = query.Where(d => EF.Functions.Like(AccentFolding.Fold(d.FileInfoFileName), pattern));
        }
        var total = await query.CountAsync();
        var items = await query.Skip(offset).Take(limit).ToListAsync();
        return (items, total);
    }

    public async Task<List<DiscoveredAudiobook>> GetByPathsAsync(List<string> paths)
    {
        return await _db.DiscoveredAudiobooks.Where(d => paths.Contains(d.FileInfoFullPath)).ToListAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var entity = await _db.DiscoveredAudiobooks.FindAsync(id);
        if (entity != null)
        {
            _db.DiscoveredAudiobooks.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task DeleteByPathAsync(string fullPath)
    {
        await _db.DiscoveredAudiobooks
            .Where(d => d.FileInfoFullPath == fullPath)
            .ExecuteDeleteAsync();

        foreach (var entry in _db.ChangeTracker.Entries<DiscoveredAudiobook>()
                     .Where(e => e.Entity.FileInfoFullPath == fullPath)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    public async Task ClearAllAsync()
    {
        // One DELETE rather than fetch-track-delete per row: a scan that just discovered
        // thousands of files would otherwise pay for all of them at the start of the next scan.
        await _db.DiscoveredAudiobooks.ExecuteDeleteAsync();

        // ExecuteDeleteAsync bypasses the change tracker; detach so a row inserted by the scan
        // that follows cannot be resolved back to a stale entity holding a reused rowid.
        foreach (var entry in _db.ChangeTracker.Entries<DiscoveredAudiobook>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
