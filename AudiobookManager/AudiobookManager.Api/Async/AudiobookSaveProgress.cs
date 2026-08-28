namespace AudiobookManager.Api.Async;

public class AudiobookSaveProgress
{
    public long AudiobookId { get; set; }
    public string ProgressMessage { get; set; }
    public int Progress { get; set; }

    public AudiobookSaveProgress(long audiobookId, string progressMessage, int progress)
    {
        AudiobookId = audiobookId;
        ProgressMessage = progressMessage;
        Progress = progress;
    }
}
