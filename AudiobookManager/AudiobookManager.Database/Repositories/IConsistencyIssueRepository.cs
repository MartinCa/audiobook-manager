using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IConsistencyIssueRepository
{
    Task<List<ConsistencyIssue>> GetAllWithAudiobookAsync();

    /// <summary>
    /// One page of issues, optionally narrowed to a single type, newest-stable-ordered the same
    /// way <see cref="GetAllWithAudiobookAsync"/> is. Returns the total matching count alongside
    /// the page so the caller can size its pager without a second round trip.
    /// </summary>
    Task<(List<ConsistencyIssue> Items, int TotalCount)> GetPageWithAudiobookAsync(
        ConsistencyIssueType? issueType, int skip, int take);

    /// <summary>How many issues of each type exist. Drives the group headers without loading them.</summary>
    Task<Dictionary<ConsistencyIssueType, int>> GetCountsByTypeAsync();

    Task<ConsistencyIssue?> GetByIdAsync(long id);
    Task InsertAsync(ConsistencyIssue issue);
    Task InsertRangeAsync(IEnumerable<ConsistencyIssue> issues);
    Task ClearAllAsync();
    Task DeleteAsync(long id);
    Task DeleteByAudiobookIdAsync(long audiobookId);
    Task DeleteByAudiobookIdAndTypesAsync(long audiobookId, IEnumerable<ConsistencyIssueType> types);
    Task<List<ConsistencyIssue>> GetByTypeAsync(ConsistencyIssueType issueType);
    Task<List<ConsistencyIssue>> GetByIdsAsync(IReadOnlyCollection<long> ids);
    Task<Dictionary<long, int>> GetIssueSummaryAsync();
    Task<List<ConsistencyIssue>> GetByAudiobookIdAsync(long audiobookId);
}
