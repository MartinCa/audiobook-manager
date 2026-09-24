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
    /// report after every item, and the configured inter-item delay. The returned
    /// <see cref="MetadataRefreshBatchResult.StopReason"/> is set when the loop stopped early
    /// (the Hardcover daily request budget was exhausted).
    /// </summary>
    Task<MetadataRefreshBatchResult> RefreshStaleAudiobooksAsync(
        DateTime? olderThanUtc,
        Func<int, int, int, int, Task> progressAction);

    /// <summary>
    /// Refreshes only the explicitly selected books, through the same per-book loop as
    /// <see cref="RefreshStaleAudiobooksAsync"/> so the two cannot drift. Unlike the stale sweep,
    /// every requested id counts toward Total: a book that did not resolve, or has no refreshable
    /// source URL, is counted Failed rather than filtered out - the user explicitly picked it.
    /// </summary>
    Task<MetadataRefreshBatchResult> RefreshSelectedAudiobooksAsync(
        IReadOnlyList<long> audiobookIds,
        Func<int, int, int, int, Task> progressAction);

    /// <summary>Deletes a book's pending snapshot, if any. Returns true when one was deleted.</summary>
    Task<bool> DismissPendingRefreshAsync(long audiobookId);

    /// <summary>
    /// The parsed pending snapshot for a book, or null when it has none (or the stored payload
    /// cannot be parsed - it is foreign data, never shown half-converted).
    /// </summary>
    Task<(PendingMetadataRefresh Row, PendingRefreshPayload.Snapshot Payload)?> GetPendingRefreshAsync(long audiobookId);

    /// <summary>
    /// One page of pending snapshots, with the book names/authors the list renders. When
    /// <paramref name="fieldsFilter"/> is non-empty, only rows whose stored changed-fields are
    /// entirely contained in it are included (a book with a Rating-only change matches a filter
    /// of {Rating, Publisher}; a book that also changed Genres does not) - null or empty means
    /// no filter.
    /// </summary>
    Task<(List<PendingMetadataRefresh> Items, int Total)> GetPendingPageAsync(
        int page, int pageSize, IReadOnlyCollection<string>? fieldsFilter = null);

    /// <summary>
    /// The sparse id list of books holding a pending snapshot, for library-list badges when
    /// <paramref name="fieldsFilter"/> is omitted, or every id matching the same subset filter
    /// <see cref="GetPendingPageAsync"/> applies - the full match set, not just one page, for
    /// "apply every book matching this filter".
    /// </summary>
    Task<List<long>> GetPendingAudiobookIdsAsync(IReadOnlyCollection<string>? fieldsFilter = null);

    /// <summary>
    /// Applies one book's pending snapshot immediately and dismisses it, for the metadata-refresh
    /// page's per-row quick apply. <paramref name="fields"/> null or empty applies every field the
    /// snapshot recorded as changed; an explicit list applies only those. Returns false only when
    /// the book has no pending snapshot (or the book itself no longer exists) - a snapshot whose
    /// stored payload cannot be parsed throws <see cref="InvalidOperationException"/> instead, so
    /// a caller can never mistake "the stored data is corrupt" for the ordinary "nothing to
    /// apply" case. A save failure (including <see cref="AudiobookBusyException"/> from the
    /// shared per-book save gate) also throws.
    /// </summary>
    Task<bool> ApplyPendingRefreshAsync(long audiobookId, IReadOnlyCollection<string>? fields = null);

    /// <summary>
    /// Applies every explicitly selected book's full pending snapshot and dismisses it, tolerating
    /// per-book failure (a book with no pending snapshot, or that failed to save, counts as
    /// Failed and the batch carries on) - the same per-item contract as
    /// <see cref="RefreshSelectedAudiobooksAsync"/>.
    /// </summary>
    Task<(int Processed, int Succeeded, int Failed)> ApplySelectedPendingRefreshesAsync(
        IReadOnlyList<long> audiobookIds, Func<int, int, int, int, Task> progressAction);

    /// <summary>
    /// Resolves every pending book whose stored changed-fields are entirely contained in
    /// <paramref name="fieldsFilter"/>, then applies each one's full snapshot the same way
    /// <see cref="ApplySelectedPendingRefreshesAsync"/> does - for "apply every book matching this
    /// filter", unbounded by page or selection size.
    /// </summary>
    Task<(int Processed, int Succeeded, int Failed)> ApplyFilteredPendingRefreshesAsync(
        IReadOnlyCollection<string> fieldsFilter, Func<int, int, int, int, Task> progressAction);

    /// <summary>
    /// Re-evaluates every pending snapshot against the library, series mapping patterns and
    /// changed-fields logic as they stand right now, without re-scraping anything - see
    /// <see cref="MetadataRefreshService.ReevaluatePendingRefreshesAsync"/> for what "re-evaluates"
    /// covers. A row that no longer differs from its book afterward is dismissed.
    /// </summary>
    Task<MetadataRefreshReevaluateResult> ReevaluatePendingRefreshesAsync();
}