namespace AudiobookManager.Services;

public interface ILibraryScanService
{
    Task ScanLibrary(Func<string, int, int, Task> progressAction);
}
