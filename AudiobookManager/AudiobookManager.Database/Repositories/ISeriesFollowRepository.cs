using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface ISeriesFollowRepository
{
    Task<bool> IsFollowedAsync(long seriesId);

    /// <summary>Idempotent: following an already-followed series is a no-op success.</summary>
    Task FollowAsync(long seriesId);

    /// <summary>Idempotent: unfollowing a series with no follow row is a no-op success.</summary>
    Task UnfollowAsync(long seriesId);

    /// <summary>
    /// Every followed series that is also matched to a source - the worker's poll list. A
    /// series followed before being matched (or after its match was cleared) is skipped until
    /// it has one.
    /// </summary>
    Task<List<Series>> GetFollowedMatchedSeriesAsync();

    /// <summary>
    /// Follow status for a batch of series values in one query, for the series overview page -
    /// the bulk counterpart of <see cref="IsFollowedAsync"/>, so rendering a page of series never
    /// costs one follow-status query per row.
    /// </summary>
    Task<HashSet<string>> GetFollowedSeriesNamesAsync(IReadOnlyCollection<string> seriesNames);
}
