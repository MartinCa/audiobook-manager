using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IConsistencyIssueRepository
{
    Task<List<ConsistencyIssue>> GetAllWithAudiobookAsync();
    Task<ConsistencyIssue?> GetByIdAsync(long id);
    Task InsertAsync(ConsistencyIssue issue);
    Task ClearAllAsync();
    Task DeleteAsync(long id);
    Task DeleteByAudiobookIdAsync(long audiobookId);
    Task DeleteByAudiobookIdAndTypesAsync(long audiobookId, IEnumerable<ConsistencyIssueType> types);
    Task<List<ConsistencyIssue>> GetByTypeAsync(ConsistencyIssueType issueType);
}
