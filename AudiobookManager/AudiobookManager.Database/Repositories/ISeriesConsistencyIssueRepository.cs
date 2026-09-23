using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface ISeriesConsistencyIssueRepository
{
    /// <summary>One page of issues with their series, newest first, plus the total matching count.</summary>
    Task<(List<SeriesConsistencyIssue> Items, int TotalCount)> GetPageWithSeriesAsync(int skip, int take);

    /// <summary>
    /// Records this series' latest refresh failure, replacing any previous one - see the entity's
    /// doc comment for why this is "the latest attempt failed", not a history.
    /// </summary>
    Task UpsertFailureAsync(long seriesId, string errorMessage);

    /// <summary>Clears a stale failure row after a refresh of this series succeeds. A no-op if there was none.</summary>
    Task DeleteBySeriesIdAsync(long seriesId);
}
