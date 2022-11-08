using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Scraping;
public interface IAudibleScraper
{
    Task<IList<BookSearchResult>> SearchAudible(string searchTerm);
}
