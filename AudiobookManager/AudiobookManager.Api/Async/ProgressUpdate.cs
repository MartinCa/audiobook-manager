namespace AudiobookManager.Api.Async;

public class ProgressUpdate
{
    public string OriginalFileLocation { get; set; }
    public string ProgressMessage { get; set; }
    public int Progress { get; set; }

    public ProgressUpdate(string originalFileLocation, string progressMessage, int progress)
    {
        OriginalFileLocation = originalFileLocation;
        ProgressMessage = progressMessage;
        Progress = progress;
    }
}
