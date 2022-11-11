using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Scraping.Scrapers;
public interface IScraper
{
    bool IsSource(string sourceName);

    bool SupportsUrl(string url);

    Task<IList<BookSearchResult>> Search(string searchTerm);

    Task<BookSearchResult> GetBookDetails(string bookUrl);
}
