namespace AudiobookManager.Api.Async;

public class LibraryScanProgress
{
    public string Message { get; set; }
    public int FilesScanned { get; set; }
    public int TotalFiles { get; set; }

    public LibraryScanProgress(string message, int filesScanned, int totalFiles)
    {
        Message = message;
        FilesScanned = filesScanned;
        TotalFiles = totalFiles;
    }
}
