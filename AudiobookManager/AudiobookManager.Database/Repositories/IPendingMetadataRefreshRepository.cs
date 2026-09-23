using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

/// <summary>
/// A book eligible for a metadata refresh, projected to exactly what the refresh engine reads:
/// the id, its source URL and the bookkeeping timestamp deciding staleness. Not the full entity
/// graph - eligibility filtering must not load descriptions and author graphs for books the
/// caller will not refresh.
/// </summary>
public record MetadataRefreshEligibleBook(long Id, string? Www, DateTime? LastMetadataRefreshedAt);

/// <summary>The lightweight projection <see cref="IPendingMetadataRefreshRepository.GetAllChangedFieldsAsync"/> reads to filter/order the pending set without loading every book graph.</summary>
public record PendingRefreshFieldsRow(long AudiobookId, DateTime FetchedAt, string? ChangedFieldsJson);

public interface IPendingMetadataRefreshRepository
{
    /// <summary>
    /// Inserts the snapshot for a book, replacing any existing one: a book holds at most one
    /// pending snapshot, and a new refresh supersedes the old.
    /// </summary>
    Task<PendingMetadataRefresh> UpsertAsync(PendingMetadataRefresh refresh);

    Task<PendingMetadataRefresh?> GetByAudiobookIdAsync(long audiobookId);

    /// <summary>Deletes a book's pending snapshot. Returns false when there was none.</summary>
    Task<bool> DeleteByAudiobookIdAsync(long audiobookId);

    /// <summary>
    /// One page of pending snapshots, newest-fetched first, with the book graph the page renders.
    /// Returns the total matching count alongside the page.
    /// </summary>
    Task<(List<PendingMetadataRefresh> Items, int TotalCount)> GetPageWithAudiobookAsync(int skip, int take);

    /// <summary>
    /// The sparse id list of books holding a pending snapshot - one entry per book that has one,
    /// not every row in the table - for library-list badges.
    /// </summary>
    Task<List<long>> GetPendingAudiobookIdsAsync();

    /// <summary>
    /// Every pending row's id, fetch time and stored changed-fields JSON - no book graph - for
    /// filtering/paging the pending set by which fields changed without loading every book.
    /// </summary>
    Task<List<PendingRefreshFieldsRow>> GetAllChangedFieldsAsync();

    /// <summary>The pending rows for the given books (no book graph included), for a bulk apply.</summary>
    Task<List<PendingMetadataRefresh>> GetByAudiobookIdsAsync(IReadOnlyCollection<long> audiobookIds);

    /// <summary>The pending rows for the given books, with the book/author graph the list renders - for a filtered page.</summary>
    Task<List<PendingMetadataRefresh>> GetByAudiobookIdsWithAudiobookAsync(IReadOnlyCollection<long> audiobookIds);

    /// <summary>Deletes all pending snapshots for books that no longer exist (defensive; the FK cascade usually covers this).</summary>
    Task<int> DeleteAllByAudiobookIdsAsync(IReadOnlyCollection<long> audiobookIds);
}