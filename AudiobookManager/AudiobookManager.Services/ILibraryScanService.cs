namespace AudiobookManager.Services;

public interface ILibraryScanService
{
    Task<(int TotalFiles, int NewFiles, int TrackedFiles)> ScanLibrary(Func<string, int, int, Task> progressAction);
}
