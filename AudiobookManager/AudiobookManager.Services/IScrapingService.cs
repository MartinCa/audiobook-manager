using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Services;
public interface IScrapingService
{
    public Task<IList<BookSearchResult>> Search(string sourceName, string searchTerm);
    public Task<BookSearchResult> GetBookDetails(string bookUrl);
    public IList<string> GetListOfScrapingServices();
}
