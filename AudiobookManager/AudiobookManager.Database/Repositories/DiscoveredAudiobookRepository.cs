using AudiobookManager.Database.Models;
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

    public async Task<(List<DiscoveredAudiobook> Items, int Total)> GetPaginatedAsync(int limit, int offset)
    {
        var query = _db.DiscoveredAudiobooks.OrderBy(d => d.FileInfoFullPath);
        var total = await query.CountAsync();
        var items = await query.Skip(offset).Take(limit).ToListAsync();
        return (items, total);
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
        var entity = await _db.DiscoveredAudiobooks.FirstOrDefaultAsync(d => d.FileInfoFullPath == fullPath);
        if (entity != null)
        {
            _db.DiscoveredAudiobooks.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task ClearAllAsync()
    {
        _db.DiscoveredAudiobooks.RemoveRange(_db.DiscoveredAudiobooks);
        await _db.SaveChangesAsync();
    }
}
