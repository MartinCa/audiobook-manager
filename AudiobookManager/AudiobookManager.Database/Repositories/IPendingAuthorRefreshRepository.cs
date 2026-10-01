using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IPendingAuthorRefreshRepository
{
    /// <summary>
    /// Stores (or replaces) the author's pending name proposal. Tolerates the read-then-insert
    /// race the same way <see cref="IAuthorConsistencyIssueRepository.UpsertFailureAsync"/> does:
    /// a refresh and a bulk sweep can both reach it for one author.
    /// </summary>
    Task UpsertAsync(long personId, string proposedName, string sourceName, string? sourceUrl);

    /// <summary>Clears the author's proposal (accepted, dismissed, or no longer applicable). A no-op if none.</summary>
    Task DeleteByPersonIdAsync(long personId);

    Task<PendingAuthorRefresh?> GetByPersonIdAsync(long personId);

    /// <summary>
    /// One page of proposals with their author, newest first with the id as the tiebreaker (a
    /// paged query needs a total order), plus the total count.
    /// </summary>
    Task<(List<PendingAuthorRefresh> Items, int TotalCount)> GetPageWithAuthorAsync(int skip, int take);

    Task<int> CountAsync();
}
