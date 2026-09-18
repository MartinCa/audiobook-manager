namespace AudiobookManager.Services;

public record AudiobookUrlCleanup(long AudiobookId, string BookName, List<string> Authors, string CurrentUrl, string CleanedUrl);

public interface IUrlCleanupService
{
    /// <summary>
    /// One page of books whose saved URL is dirty - a query string or fragment
    /// <see cref="AudiobookManager.Scraping.Utils.BookUrlCleaner"/> would strip - plus how many
    /// there are in total, so the client can render a pager. Paged rather than returned whole:
    /// with a few thousand dirty URLs the unpaged response was a multi-megabyte payload parsed
    /// and held to render every card into the DOM at once.
    /// </summary>
    Task<(List<AudiobookUrlCleanup> Items, int Total)> FindDirtyUrlsPageAsync(int page, int pageSize);

    Task<int> ApplyAsync(IEnumerable<long> audiobookIds);

    /// <summary>
    /// Cleans every currently-dirty book in one operation, not just the page a client happens to
    /// be looking at. The dirty id set is discovered by paging through
    /// <see cref="AudiobookManager.Database.Repositories.IAudiobookRepository.GetDirtyUrlPageAsync"/>
    /// - the same predicate the page endpoint reads - rather than loading every dirty row at once,
    /// so the memory cost stays bounded per the repository's bounded-list invariant: only the ids
    /// are kept for the batch, never the row objects (which carry the book name and author list).
    /// Progress is reported per item via <c>progressAction</c>.
    /// </summary>
    Task<(int Processed, int Succeeded, int Failed)> ApplyAllAsync(Func<int, int, int, int, Task>? progressAction);
}
