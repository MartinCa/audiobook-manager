namespace AudiobookManager.Services;

/// <summary>
/// Reconciles a matched author's roster against the author's owned books - the author-detail
/// counterpart of <see cref="ISeriesReconciliationProvider"/>. The roster lives on the unified
/// <see cref="AudiobookManager.Database.Models.ExpectedBook"/> table and spans the whole
/// bibliography, series books included (the same source book discovered by an author refresh and
/// by a series refresh is one row, linked to both scopes). No caching layer of its own: an
/// author's roster is bounded the same way the series roster is, so this recomputes per request
/// rather than carrying the extra cache-invalidation surface
/// <see cref="ISeriesReconciliationCache"/> needs for the much larger, much more frequently
/// re-read series detail page.
/// </summary>
public interface IAuthorReconciliationProvider
{
    /// <summary>
    /// Reconciles a matched author's roster against their owned books. <paramref name="includeMissingSeries"/>
    /// controls the source-series-groups computation (<see cref="AuthorReconciliation.MissingSeries"/>),
    /// a bounded-but-non-trivial per-author pass the author-detail page renders. The global
    /// upcoming view only needs the Missing/Upcoming classification, so it opts out - a
    /// pathological group set that would refuse the detail computation must never take down the
    /// whole upcoming page (the bounded cap still applies whenever the detail asks for it).
    /// </summary>
    Task<AuthorReconciliation> GetReconciliationAsync(long personId, bool includeMissingSeries = true);

    /// <summary>
    /// Which authors have at least one unmatched, non-upcoming ("missing") roster entry, and
    /// which have at least one unmatched, not-yet-released ("upcoming") one - the authors list
    /// filter's bulk counterpart of <see cref="GetReconciliationAsync"/>. One whole-library
    /// computation rather than one reconciliation per author: the unified roster is read through a
    /// single bounded query (<see cref="AuthorReconciliationProvider.MaxBulkReconciliationRefs"/>
    /// refs plus an overflow flag), and each author's slice is then clamped to the same
    /// per-author caps the detail view enforces. A library past the global cap cannot be
    /// classified safely - the result reports <see cref="AuthorBulkReconciliationResult.Refused"/>
    /// and the caller skips the affected filter rather than silently truncating a growing table.
    /// Only runs when the authors list actually asks for one of these two filters.
    /// </summary>
    Task<AuthorBulkReconciliationResult> GetBulkMissingOrUpcomingAuthorIdsAsync();
}

/// <summary>
/// The bulk authors-list filter result. <see cref="Refused"/> is set when the whole-library
/// reference set exceeded the bounded read's cap: the filter is then skipped by the caller
/// (the list keeps its other filters; the missing/upcoming one is simply not applied) rather than
/// applied to a truncated set, which would produce a silently-wrong result.
/// </summary>
public record AuthorBulkReconciliationResult(
    HashSet<long> HasMissingBooks,
    HashSet<long> HasUpcomingBooks,
    bool Refused);