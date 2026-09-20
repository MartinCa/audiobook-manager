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
    /// Sorts the caller-supplied library directory walk deepest-first and finds the reclaimable
    /// folders, inserting the findings and reporting one final progress update. Returns
    /// <paramref name="issuesFound"/> plus whatever this sweep found, for the caller to fold into
    /// its own running total. The walk itself is done once by the combined scan's
    /// <see cref="AudiobookManager.FileManager.LibraryTreeWalker"/> and handed in, so the sweep
    /// does not re-walk the library.
    /// </summary>
    Task<int> ScanAsync(
        Func<string, int, int, int, Task> progressAction,
        int totalBooks,
        int issuesFound,
        IReadOnlyList<AudiobookManager.FileManager.LibraryDirectory> directories);

    Task<OrphanDirectoryResolveResult> ResolveOrphanDirectory(long orphanDirectoryId);

    Task<(int resolved, int failed, int retained)> ResolveAllOrphanDirectories();
}
