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
}
