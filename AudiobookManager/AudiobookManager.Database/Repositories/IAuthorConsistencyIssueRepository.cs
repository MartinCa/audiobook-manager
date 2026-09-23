using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IAuthorConsistencyIssueRepository
{
    /// <summary>One page of issues with their author, newest first, plus the total matching count.</summary>
    Task<(List<AuthorConsistencyIssue> Items, int TotalCount)> GetPageWithAuthorAsync(int skip, int take);

    /// <summary>
    /// Records this author's latest roster-refresh failure, replacing any previous one - see the
    /// entity's doc comment for why this is "the latest attempt failed", not a history.
    /// </summary>
    Task UpsertFailureAsync(long personId, string errorMessage);

    /// <summary>Clears a stale failure row after a refresh of this author succeeds. A no-op if there was none.</summary>
    Task DeleteByPersonIdAsync(long personId);
}
