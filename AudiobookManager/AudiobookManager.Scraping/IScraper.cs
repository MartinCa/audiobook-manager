using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Scraping;
public interface IScraper
{
    bool IsSource(string sourceName);

    bool SupportsUrl(string url);

    Task<IList<BookSearchResult>> Search(string searchTerm);

    Task<BookSearchResult> GetBookDetails(string bookUrl);
}
