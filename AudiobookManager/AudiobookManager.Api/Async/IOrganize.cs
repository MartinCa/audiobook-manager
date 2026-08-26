namespace AudiobookManager.Api.Async;

public interface IOrganize
{
    Task UpdateProgress(ProgressUpdate progressUpdate);
    Task QueueError(QueueError queueError);
    Task LibraryScanProgress(LibraryScanProgress progress);
    Task LibraryScanComplete(LibraryScanComplete result);
    Task ConsistencyCheckProgress(ConsistencyCheckProgress progress);
    Task ConsistencyCheckComplete(ConsistencyCheckComplete result);
    Task SimilarValueAlignProgress(SimilarValueAlignProgress progress);
    Task SimilarValueAlignComplete(SimilarValueAlignComplete result);
    Task DiscoveredImportProgress(DiscoveredImportProgress progress);
    Task DiscoveredImportComplete(DiscoveredImportComplete result);
}
