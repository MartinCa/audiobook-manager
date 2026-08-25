using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class OrphanDirectoryRepository : IOrphanDirectoryRepository
{
    private readonly DatabaseContext _db;

    public OrphanDirectoryRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<List<OrphanDirectory>> GetAllAsync()
    {
        return await _db.OrphanDirectories.OrderBy(d => d.DirectoryPath).ToListAsync();
    }

    public async Task<OrphanDirectory?> GetByIdAsync(long id)
    {
        return await _db.OrphanDirectories.FindAsync(id);
    }

    public async Task InsertAsync(OrphanDirectory directory)
    {
        _db.Add(directory);
        await _db.SaveChangesAsync();
    }

    public async Task ClearAllAsync()
    {
        _db.OrphanDirectories.RemoveRange(_db.OrphanDirectories);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var entity = await _db.OrphanDirectories.FindAsync(id);
        if (entity != null)
        {
            _db.OrphanDirectories.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }
}
