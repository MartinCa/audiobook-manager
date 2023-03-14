namespace AudiobookManager.Domain;
public class QueuedOrganizeTask
{
    public string OriginalFileLocation { get; set; }

    public Audiobook Audiobook { get; set; }

    public DateTime? QueuedTime { get; set; }

    public QueuedOrganizeTask(string originalFileLocation, Audiobook audiobook, DateTime? queuedTime)
    {
        OriginalFileLocation = originalFileLocation;
        Audiobook = audiobook;
        QueuedTime = queuedTime;
    }
}
