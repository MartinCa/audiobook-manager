using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Scraping.Scrapers;
public interface IScraper
{
    string SourceName { get; }

    bool RequiresApiKey => false;

    bool IsApiKeyConfigured => true;

    bool IsSource(string sourceName);

    bool SupportsUrl(string url);

    Task<IList<MetadataSearchResult>> Search(string searchTerm);

    Task<MetadataSearchResult> GetBookDetails(string bookUrl);

    /// <summary>
    /// Whether this source can look up a whole series roster (used by the series catalog
    /// to detect missing books). Optional capability - sources that only do per-book
    /// lookups leave the defaults below in place.
    /// </summary>
    bool SupportsSeriesLookup => false;

    Task<IList<SeriesSearchResult>> SearchSeries(string searchTerm) =>
        Task.FromResult<IList<SeriesSearchResult>>(new List<SeriesSearchResult>());

    /// <summary>
    /// Fetches a series and its full book roster by the source id returned from
    /// <see cref="SearchSeries"/> (a source URL is also accepted).
    /// </summary>
    Task<SeriesSearchResult?> GetSeriesBooks(string seriesIdOrUrl) =>
        Task.FromResult<SeriesSearchResult?>(null);
}
