using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Services;
public interface IScrapingService
{
    public Task<IList<BookSearchResult>> SearchAudible(string searchTerm);
}
