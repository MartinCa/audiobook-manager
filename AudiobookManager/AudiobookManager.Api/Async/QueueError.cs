namespace AudiobookManager.Api.Async;

public class QueueError
{
    public string OriginalFileLocation { get; set; }
    public string Error { get; set; }

    public QueueError(string originalFileLocation, string error)
    {
        OriginalFileLocation = originalFileLocation;
        Error = error;
    }
}
