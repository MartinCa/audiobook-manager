using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IBookConsistencyIssueRepository
{
    Task<List<BookConsistencyIssue>> GetAllWithAudiobookAsync();

    /// <summary>
    /// One page of issues, optionally narrowed to a single type, newest-stable-ordered the same
    /// way <see cref="GetAllWithAudiobookAsync"/> is. Returns the total matching count alongside
    /// the page so the caller can size its pager without a second round trip.
    /// </summary>
    Task<(List<BookConsistencyIssue> Items, int TotalCount)> GetPageWithAudiobookAsync(
        BookConsistencyIssueType? issueType, int skip, int take);

    /// <summary>How many issues of each type exist. Drives the group headers without loading them.</summary>
    Task<Dictionary<BookConsistencyIssueType, int>> GetCountsByTypeAsync();

    Task<BookConsistencyIssue?> GetByIdAsync(long id);
    Task InsertAsync(BookConsistencyIssue issue);
    Task InsertRangeAsync(IEnumerable<BookConsistencyIssue> issues);
    Task ClearAllAsync();
    Task DeleteAsync(long id);
    Task DeleteByAudiobookIdAsync(long audiobookId);
    Task DeleteByAudiobookIdAndTypesAsync(long audiobookId, IEnumerable<BookConsistencyIssueType> types);
    Task<List<BookConsistencyIssue>> GetByTypeAsync(BookConsistencyIssueType issueType);
    Task<List<BookConsistencyIssue>> GetByIdsAsync(IReadOnlyCollection<long> ids);
    Task<Dictionary<long, int>> GetIssueSummaryAsync();
    Task<List<BookConsistencyIssue>> GetByAudiobookIdAsync(long audiobookId);

    /// <summary>Saves changes to an already-tracked issue (an edit, not an insert or delete).</summary>
    Task UpdateAsync(BookConsistencyIssue issue);
}
