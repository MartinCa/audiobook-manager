using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

/// <summary>
/// A book eligible for a metadata refresh, projected to exactly what the refresh engine reads:
/// the id, its source URL and the bookkeeping timestamp deciding staleness. Not the full entity
/// graph - eligibility filtering must not load descriptions and author graphs for books the
/// caller will not refresh.
/// </summary>
public record MetadataRefreshEligibleBook(long Id, string? Www, DateTime? LastMetadataRefreshedAt);

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

    /// <summary>Deletes all pending snapshots for books that no longer exist (defensive; the FK cascade usually covers this).</summary>
    Task<int> DeleteAllByAudiobookIdsAsync(IReadOnlyCollection<long> audiobookIds);
}