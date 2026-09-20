namespace AudiobookManager.Services;

/// <summary>
/// Runs the combined library scan: file discovery and the library-wide consistency check as one
/// operation behind one gate. The two used to be separate endpoints that each walked the whole
/// library and each loaded the tracked graph; this orchestrator performs each of those once and
/// shares the result, leaving per-file parsing where it already was (the scan parses untracked
/// files, the consistency check its tracked books).
/// </summary>
public interface ILibraryScanOrchestrator
{
    /// <summary>
    /// Guards the library, clears the discovered table, loads the tracked graph once, walks the
    /// library once, scans the untracked files, then runs the consistency check over the loaded
    /// graph and directory walk.
    /// </summary>
    /// <param name="discoveryCompleted">
    /// Invoked with the scan's real totals as soon as <see cref="ILibraryScanService.ScanFilesAsync"/>
    /// returns and before the consistency half starts, so the caller can report the scan's own
    /// completion before the (potentially slow) consistency check and never has to zero it if that
    /// later half fails.
    /// </param>
    Task<CombinedScanResult> RunCombinedScanAsync(
        Func<string, int, int, Task> discoveryProgress,
        Func<string, int, int, int, Task> consistencyProgress,
        Func<int, int, int, Task>? discoveryCompleted = null);
}
