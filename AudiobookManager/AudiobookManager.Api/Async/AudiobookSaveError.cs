namespace AudiobookManager.Api.Async;

public class AudiobookSaveError
{
    public long AudiobookId { get; set; }
    public string Error { get; set; }

    public AudiobookSaveError(long audiobookId, string error)
    {
        AudiobookId = audiobookId;
        Error = error;
    }
}
