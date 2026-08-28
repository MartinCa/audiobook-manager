namespace AudiobookManager.Api.Async;

public class AudiobookSaveComplete
{
    public long AudiobookId { get; set; }

    public AudiobookSaveComplete(long audiobookId)
    {
        AudiobookId = audiobookId;
    }
}
