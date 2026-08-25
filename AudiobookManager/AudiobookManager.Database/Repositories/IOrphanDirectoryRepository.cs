using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IOrphanDirectoryRepository
{
    Task<List<OrphanDirectory>> GetAllAsync();
    Task<OrphanDirectory?> GetByIdAsync(long id);
    Task InsertAsync(OrphanDirectory directory);
    Task ClearAllAsync();
    Task DeleteAsync(long id);
}
