using AudiobookManager.Database.Models;
using AudiobookManager.Domain;

namespace AudiobookManager.Services;

public interface IMetadataRefreshService
{
    /// <summary>
    /// Whether any registered scraper can refresh a book whose source URL is
    /// <paramref name="url"/>. Non-throwing, so callers filter eligibility with it instead of
    /// catching the exception <see cref="IScrapingService.GetBookDetails"/> throws for an
    /// unsupported URL.
    /// </summary>
    bool CanRefresh(string? url);

    /// <summary>
    /// Refreshes one book from the source its Www URL points at. Never throws for an ordinary
    /// scrape failure - that comes back as <see cref="MetadataRefreshResult.Success"/> false -
    /// because the bulk loop counts failures per book and carries on.
    /// </summary>
    Task<MetadataRefreshResult> RefreshAudiobookAsync(long audiobookId);

    /// <summary>
    /// Refreshes every book that is eligible (has a URL a scraper supports, and is stale per
    /// <paramref name="olderThanUtc"/> - null meaning never-refreshed counts as stale) with the
    /// shared bulk contract: per-item try/catch, a (processed, total, succeeded, failed) progress
    /// report after every item, and the configured inter-item delay.
    /// </summary>
    Task<(int Processed, int Total, int Succeeded, int Failed)> RefreshStaleAudiobooksAsync(
        DateTime? olderThanUtc,
        Func<int, int, int, int, Task> progressAction);

    /// <summary>Deletes a book's pending snapshot, if any. Returns true when one was deleted.</summary>
    Task<bool> DismissPendingRefreshAsync(long audiobookId);

    /// <summary>
    /// The parsed pending snapshot for a book, or null when it has none (or the stored payload
    /// cannot be parsed - it is foreign data, never shown half-converted).
    /// </summary>
    Task<(PendingMetadataRefresh Row, PendingRefreshPayload.Snapshot Payload)?> GetPendingRefreshAsync(long audiobookId);

    /// <summary>One page of pending snapshots, with the book names/authors the list renders.</summary>
    Task<(List<PendingMetadataRefresh> Items, int Total)> GetPendingPageAsync(int page, int pageSize);

    /// <summary>The sparse id list of books holding a pending snapshot, for library-list badges.</summary>
    Task<List<long>> GetPendingAudiobookIdsAsync();
}