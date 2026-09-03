namespace AudiobookManager.Services;

/// <summary>
/// Detects and resolves orphaned library directories - folders left behind with no audio file
/// anywhere in their subtree. A separate concern from <see cref="ConsistencyIssue"/> handling:
/// orphan directories are not tied to an audiobook record at all, they're their own
/// <c>OrphanDirectory</c> model with its own table.
/// </summary>
public interface IOrphanDirectoryConsistencyService
{
    /// <summary>
    /// Clears the stored orphan-directory list. Split out from <see cref="ScanAsync"/> so the
    /// orchestrator (<see cref="LibraryConsistencyService.RunConsistencyCheck"/>) can clear both
    /// the issue and orphan tables up front, before any work starts - matching the issue table's
    /// lifecycle rather than leaving stale orphan rows behind a run that fails before reaching
    /// the sweep.
    /// </summary>
    Task ClearAllAsync();

    /// <summary>
    /// Re-walks the whole library for orphaned directories, inserting the findings and reporting
    /// one final progress update. Returns <paramref name="issuesFound"/> plus whatever this sweep
    /// found, for the caller to fold into its own running total.
    /// </summary>
    Task<int> ScanAsync(Func<string, int, int, int, Task> progressAction, int totalBooks, int issuesFound);

    Task<OrphanDirectoryResolveResult> ResolveOrphanDirectory(long orphanDirectoryId);

    Task<(int resolved, int failed, int retained)> ResolveAllOrphanDirectories();
}
