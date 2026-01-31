using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IDiscoveredAudiobookRepository
{
    Task InsertAsync(DiscoveredAudiobook discovered);
    Task<List<DiscoveredAudiobook>> GetAllAsync();
    Task DeleteAsync(long id);
    Task DeleteByPathAsync(string fullPath);
    Task ClearAllAsync();
}
