namespace AudiobookManager.Api.Async;

public class LibraryScanComplete
{
    public int TotalFilesScanned { get; set; }
    public int NewFilesDiscovered { get; set; }
    public int AlreadyTracked { get; set; }

    public LibraryScanComplete(int totalFilesScanned, int newFilesDiscovered, int alreadyTracked)
    {
        TotalFilesScanned = totalFilesScanned;
        NewFilesDiscovered = newFilesDiscovered;
        AlreadyTracked = alreadyTracked;
    }
}
