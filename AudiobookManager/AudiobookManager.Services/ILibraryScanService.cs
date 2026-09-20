using AudiobookManager.Database.Models;
using AudiobookManager.Domain;

namespace AudiobookManager.Services;

public interface ILibraryScanService
{
    /// <summary>
    /// Classifies the already-walked supported files against the already-loaded known paths,
    /// parsing and inserting the untracked ones. The caller (<see cref="ILibraryScanOrchestrator"/>)
    /// performed the directory walk and the tracked-graph load so they can be shared with the
    /// consistency check.
    /// </summary>
    Task<(int TotalFiles, int NewFiles, int TrackedFiles)> ScanFilesAsync(
        IReadOnlyList<AudiobookFileInfo> files,
        IReadOnlyCollection<string> knownPaths,
        Func<string, int, int, Task> progressAction);

    Task<(int Processed, int Succeeded, int Failed)> BulkImportAsync(
        List<string> filePaths,
        Func<int, int, int, int, Task> progressAction,
        Func<string, string, Task>? onItemFailed = null);

    Task<(int Processed, int Succeeded, int Failed)> BulkImportAllWellTaggedAsync(
        Func<int, int, int, int, Task> progressAction,
        Func<string, string, Task>? onItemFailed = null);

    /// <summary>
    /// Whether a discovered entry's generated library path is already occupied by another file,
    /// so bulk import would fail. Returns false for an entry missing the tags required to
    /// generate a path (author/book name/year) rather than throwing.
    /// </summary>
    bool IsDuplicateTarget(DiscoveredAudiobook entry);
}
