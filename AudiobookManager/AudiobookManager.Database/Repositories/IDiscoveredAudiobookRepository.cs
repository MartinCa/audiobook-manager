using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IDiscoveredAudiobookRepository
{
    Task InsertAsync(DiscoveredAudiobook discovered);
    Task InsertRangeAsync(IEnumerable<DiscoveredAudiobook> discovered);
    Task<List<DiscoveredAudiobook>> GetAllAsync();
    Task<(List<DiscoveredAudiobook> Items, int Total)> GetPaginatedAsync(int limit, int offset, string? search = null);
    Task<List<DiscoveredAudiobook>> GetByPathsAsync(List<string> paths);
    Task DeleteAsync(long id);
    Task DeleteByPathAsync(string fullPath);
    Task ClearAllAsync();
}
