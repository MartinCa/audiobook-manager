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
        return await _db.OrphanDirectories.AsNoTracking().OrderBy(d => d.DirectoryPath).ToListAsync();
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

    public async Task InsertRangeAsync(IEnumerable<OrphanDirectory> directories)
    {
        var list = directories as ICollection<OrphanDirectory> ?? directories.ToList();
        if (list.Count == 0)
        {
            return;
        }

        _db.AddRange(list);
        await _db.SaveChangesAsync();
    }

    public async Task ClearAllAsync()
    {
        await _db.OrphanDirectories.ExecuteDeleteAsync();

        foreach (var entry in _db.ChangeTracker.Entries<OrphanDirectory>().ToList())
        {
            entry.State = EntityState.Detached;
        }
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
