namespace AudiobookManager.Services;

public interface ILibraryScanService
{
    Task<(int TotalFiles, int NewFiles, int TrackedFiles)> ScanLibrary(Func<string, int, int, Task> progressAction);

    Task<(int Processed, int Succeeded, int Failed)> BulkImportAsync(
        List<string> filePaths,
        Func<int, int, int, int, Task> progressAction);
}
