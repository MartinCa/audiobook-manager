using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IPendingSeriesRefreshRepository
{
    /// <summary>One series' pending snapshot, or null when none exists.</summary>
    Task<PendingSeriesRefresh?> GetBySeriesNameAsync(string seriesName);

    /// <summary>
    /// Replaces the pending snapshot for a series (one per series - a fresh refresh supersedes the
    /// old one). Used by the refresh flow; a no-change refresh deletes instead of upserting so a
    /// no-change bulk item never lingers in the pending list.
    /// </summary>
    Task<PendingSeriesRefresh> UpsertAsync(PendingSeriesRefresh pending);

    /// <summary>Deletes the pending snapshot for a series; false when none existed.</summary>
    Task<bool> DeleteBySeriesNameAsync(string seriesName);

    /// <summary>
    /// One page of the pending list, newest fetch first. Rows exist only for series whose refresh
    /// produced changes, so this is the "no-change bulk items must not appear" guarantee's read
    /// side.
    /// </summary>
    Task<(List<PendingSeriesRefresh> Items, int TotalCount)> GetPageAsync(int skip, int take);

    /// <summary>The number of series with a pending snapshot, for the list header badge.</summary>
    Task<int> CountAsync();
}