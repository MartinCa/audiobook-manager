using AudiobookManager.Database.Models;

namespace AudiobookManager.Services;

public interface ILibraryScanService
{
    Task<(int TotalFiles, int NewFiles, int TrackedFiles)> ScanLibrary(Func<string, int, int, Task> progressAction);

    Task<(int Processed, int Succeeded, int Failed)> BulkImportAsync(
        List<string> filePaths,
        Func<int, int, int, int, Task> progressAction,
        Func<string, string, Task>? onItemFailed = null);

    /// <summary>
    /// Whether a discovered entry's generated library path is already occupied by another file,
    /// so bulk import would fail. Returns false for an entry missing the tags required to
    /// generate a path (author/book name/year) rather than throwing.
    /// </summary>
    bool IsDuplicateTarget(DiscoveredAudiobook entry);
}
