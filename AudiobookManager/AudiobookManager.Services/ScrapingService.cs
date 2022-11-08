using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Services;
public class ScrapingService : IScrapingService
{
    private readonly IAudibleScraper _audibleScraper;

    public ScrapingService(IAudibleScraper audibleScraper)
    {
        _audibleScraper = audibleScraper;
    }

    public Task<IList<BookSearchResult>> SearchAudible(string searchTerm)
    {
        return _audibleScraper.SearchAudible(searchTerm);
    }
}
