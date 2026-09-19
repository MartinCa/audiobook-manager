namespace AudiobookManager.Services;

/// <summary>
/// Reconciles a matched author's standalone-books roster against the author's owned standalone
/// books - the author-detail counterpart of <see cref="ISeriesReconciliationProvider"/>. No
/// caching layer of its own: an author's standalone bibliography is small (bounded the same way
/// the series roster is), so this recomputes per request rather than carrying the extra
/// cache-invalidation surface <see cref="ISeriesReconciliationCache"/> needs for the much larger,
/// much more frequently re-read series detail page.
/// </summary>
public interface IAuthorReconciliationProvider
{
    Task<AuthorReconciliation> GetReconciliationAsync(long personId);

    /// <summary>
    /// Which authors have at least one unmatched, non-upcoming ("missing") roster entry, and
    /// which have at least one unmatched, not-yet-released ("upcoming") one - the authors list
    /// filter's bulk counterpart of <see cref="GetReconciliationAsync"/>. One whole-library
    /// computation (bounded the same way a single reconciliation is - see
    /// <see cref="AuthorReconciliationProvider"/>) rather than one reconciliation per author, so
    /// the filter costs one pass regardless of how many authors have a roster. Only runs when the
    /// authors list actually asks for one of these two filters.
    /// </summary>
    Task<(HashSet<long> HasMissingBooks, HashSet<long> HasUpcomingBooks)> GetBulkMissingOrUpcomingAuthorIdsAsync();
}
