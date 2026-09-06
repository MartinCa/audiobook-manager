namespace AudiobookManager.Domain;
public class QueuedOrganizeTask
{
    public string OriginalFileLocation { get; set; }

    public Audiobook Audiobook { get; set; }

    public DateTime? QueuedTime { get; set; }

    /// <summary>
    /// Explicit request metadata kept outside the audiobook domain object so ordinary audiobook
    /// updates cannot accidentally opt into metadata-refresh bookkeeping.
    /// </summary>
    public bool MetadataAppliedFromSearch { get; set; }

    public QueuedOrganizeTask(string originalFileLocation, Audiobook audiobook, DateTime? queuedTime, bool metadataAppliedFromSearch = false)
    {
        OriginalFileLocation = originalFileLocation;
        Audiobook = audiobook;
        QueuedTime = queuedTime;
        MetadataAppliedFromSearch = metadataAppliedFromSearch;
    }
}
