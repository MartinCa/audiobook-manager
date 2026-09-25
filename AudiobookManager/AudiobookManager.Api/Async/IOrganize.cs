namespace AudiobookManager.Api.Async;

public interface IOrganize
{
    Task UpdateProgress(ProgressUpdate progressUpdate);
    Task QueueError(QueueError queueError);
    Task LibraryScanProgress(LibraryScanProgress progress);
    Task LibraryScanComplete(LibraryScanComplete result);
    Task ConsistencyCheckProgress(ConsistencyCheckProgress progress);
    Task ConsistencyCheckComplete(ConsistencyCheckComplete result);
    Task ConsistencyResolveProgress(ConsistencyResolveProgress progress);
    Task ConsistencyResolveComplete(ConsistencyResolveComplete result);
    Task SimilarValueAlignProgress(SimilarValueAlignProgress progress);
    Task SimilarValueAlignComplete(SimilarValueAlignComplete result);
    Task DiscoveredImportProgress(DiscoveredImportProgress progress);
    Task DiscoveredImportComplete(DiscoveredImportComplete result);
    Task SeriesMatchProgress(SeriesMatchProgress progress);
    Task SeriesMatchComplete(SeriesMatchComplete result);
    Task SeriesRefreshProgress(SeriesRefreshProgress progress);
    Task SeriesRefreshComplete(SeriesRefreshComplete result);
    Task SeriesRefreshApplyProgress(SeriesRefreshApplyProgress progress);
    Task SeriesRefreshApplyComplete(SeriesRefreshApplyComplete result);
    Task SeriesMissingBookApplyProgress(SeriesMissingBookApplyProgress progress);
    Task SeriesMissingBookApplyComplete(SeriesMissingBookApplyComplete result);
    Task SeriesDeleteProgress(SeriesDeleteProgress progress);
    Task SeriesDeleteComplete(SeriesDeleteComplete result);
    Task AudiobookSaveProgress(AudiobookSaveProgress progress);
    Task AudiobookSaveComplete(AudiobookSaveComplete result);
    Task AudiobookSaveError(AudiobookSaveError error);
    Task MetadataRefreshProgress(MetadataRefreshProgress progress);
    Task MetadataRefreshComplete(MetadataRefreshComplete result);
    Task MetadataApplyProgress(MetadataApplyProgress progress);
    Task MetadataApplyComplete(MetadataApplyComplete result);
    Task BulkEditProgress(BulkEditProgress progress);
    Task BulkEditComplete(BulkEditComplete result);
    Task UrlCleanupProgress(UrlCleanupProgress progress);
    Task UrlCleanupComplete(UrlCleanupComplete result);
    Task PendingOnlineMatchSearchProgress(PendingOnlineMatchSearchProgress progress);
    Task PendingOnlineMatchSearchComplete(PendingOnlineMatchSearchComplete result);
}
