using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Services;
public interface IScrapingService
{
    public Task<IList<MetadataSearchResult>> Search(string sourceName, string searchTerm);
    public Task<MetadataMultiSourceSearchResult> SearchMultiple(IEnumerable<string> sourceNames, string searchTerm);
    public Task<MetadataSearchResult> GetBookDetails(string bookUrl);
    public IList<string> GetListOfScrapingServices();
    public IList<MetadataSearchServiceInfo> GetSearchServiceInfo();
}
