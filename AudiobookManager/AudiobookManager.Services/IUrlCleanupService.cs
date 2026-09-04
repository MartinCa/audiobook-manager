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
}
