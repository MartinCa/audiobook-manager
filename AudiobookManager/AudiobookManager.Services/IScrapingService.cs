using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Services;
public interface IScrapingService
{
    public Task<IList<MetadataSearchResult>> Search(string sourceName, string searchTerm);
    public Task<MetadataMultiSourceSearchResult> SearchMultiple(IEnumerable<string> sourceNames, string searchTerm);

    /// <summary>
    /// Searches each source in <paramref name="queriesBySource"/> with its own query (the default
    /// query is written per source - see <c>MetadataSearchQueryBuilder.BuildPerSource</c>). The keys
    /// are the sources to search, matched like the names in the single-query overload.
    /// </summary>
    public Task<MetadataMultiSourceSearchResult> SearchMultiple(IReadOnlyDictionary<string, string> queriesBySource);
    public Task<MetadataSearchResult> GetBookDetails(string bookUrl);
    public IList<string> GetListOfScrapingServices();
    /// <summary>Every source, in the order chosen in Library Settings.</summary>
    public Task<IList<MetadataSearchServiceInfo>> GetSearchServiceInfo();
}
