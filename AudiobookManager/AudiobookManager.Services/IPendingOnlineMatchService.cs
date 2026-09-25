using AudiobookManager.Database.Models;

namespace AudiobookManager.Services;

public record PendingOnlineMatchBatchResult(int Processed, int Total, int Succeeded, int Failed);

public interface IPendingOnlineMatchService
{
    /// <summary>
    /// Searches every explicitly selected book across the given sources, using the same
    /// default-query precedence the interactive "Search Online Metadata" dialog seeds itself with
    /// (book name, falling back to the on-disk file name) - so changing that precedence changes
    /// both flows together. Stores each book's results (even zero of them) as a Pending row,
    /// replacing any row it already had. Per-item try/catch: a book whose search itself throws
    /// counts Failed and the batch carries on, the same bulk contract every other background
    /// operation in this codebase shares.
    /// </summary>
    Task<PendingOnlineMatchBatchResult> SearchSelectedAsync(
        IReadOnlyList<long> audiobookIds,
        IReadOnlyList<string> sourceNames,
        Func<int, int, int, int, Task> progressAction);

    /// <summary>
    /// Fetches full details for the chosen candidate (by its position in the stored results) and
    /// records them as a pending metadata-refresh snapshot via
    /// <see cref="IMetadataRefreshService.ApplyFetchedResultAsSnapshotAsync"/> - the same review/
    /// apply flow a book's own "Refresh Now" uses. The online-match row is then deleted: this book
    /// is resolved, whether or not the fetch actually produced a diff (a diff-less fetch still
    /// means the search successfully confirmed a match). Throws <see cref="KeyNotFoundException"/>
    /// when the book has no pending online-match row, and <see cref="ArgumentOutOfRangeException"/>
    /// when the index does not name one of its stored candidates.
    /// </summary>
    Task SelectResultAsync(long audiobookId, int resultIndex);

    /// <summary>Marks a book's row Rejected (moves it to the Failed/Rejected list). Returns false if it had none.</summary>
    Task<bool> RejectAsync(long audiobookId);

    /// <summary>Deletes a book's row outright - the Failed/Rejected list's "Dismiss". Returns false if it had none.</summary>
    Task<bool> DismissAsync(long audiobookId);

    Task<(List<PendingOnlineMatch> Items, int Total)> GetPageAsync(
        PendingOnlineMatchStatus status, int page, int pageSize);
}
