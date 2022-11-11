using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.Scrapers;

namespace AudiobookManager.Services;
public class ScrapingService : IScrapingService
{
    private readonly IEnumerable<IScraper> _scrapers;

    public ScrapingService(IEnumerable<IScraper> scrapers)
    {
        _scrapers = scrapers;
    }

    public Task<IList<BookSearchResult>> Search(string sourceName, string searchTerm)
    {
        var scraper = _scrapers.SingleOrDefault(s => s.IsSource(sourceName));

        if (scraper == default)
        {
            throw new Exception($"No scraper for source {sourceName}");
        }

        return scraper.Search(searchTerm);
    }

    public Task<BookSearchResult> GetBookDetails(string bookUrl)
    {
        var scraper = _scrapers.SingleOrDefault(s => s.SupportsUrl(bookUrl));

        if (scraper == default)
        {
            throw new Exception($"No scraper supports url {bookUrl}");
        }

        return scraper.GetBookDetails(bookUrl);
    }
}
