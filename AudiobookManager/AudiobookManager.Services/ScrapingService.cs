using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.Scrapers;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;
public class ScrapingService : IScrapingService
{
    private readonly IEnumerable<IScraper> _scrapers;
    private readonly ILogger<ScrapingService> _logger;

    public ScrapingService(IEnumerable<IScraper> scrapers, ILogger<ScrapingService> logger)
    {
        _scrapers = scrapers;
        _logger = logger;
    }

    public async Task<IList<BookSearchResult>> Search(string sourceName, string searchTerm)
    {
        var scraper = _scrapers.SingleOrDefault(s => s.IsSource(sourceName));

        if (scraper == default)
        {
            throw new Exception($"No scraper for source {sourceName}");
        }

        var results = await scraper.Search(searchTerm);
        foreach (var result in results)
        {
            result.Source = scraper.SourceName;
        }

        return results;
    }

    public async Task<MultiSourceSearchResult> SearchMultiple(IEnumerable<string> sourceNames, string searchTerm)
    {
        var scrapers = _scrapers.Where(s => sourceNames.Any(s.IsSource)).ToList();

        var searchTasks = scrapers.Select(async scraper =>
        {
            try
            {
                var results = await scraper.Search(searchTerm);
                foreach (var result in results)
                {
                    result.Source = scraper.SourceName;
                }

                return (Status: new SourceSearchStatus
                {
                    Source = scraper.SourceName,
                    Success = true,
                    ResultCount = results.Count,
                }, Results: results);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search failed for source {Source}", scraper.SourceName);

                return (Status: new SourceSearchStatus
                {
                    Source = scraper.SourceName,
                    Success = false,
                    ResultCount = 0,
                    Error = ex.Message,
                }, Results: (IList<BookSearchResult>)new List<BookSearchResult>());
            }
        });

        var outcomes = await Task.WhenAll(searchTasks);

        return new MultiSourceSearchResult
        {
            Results = outcomes.SelectMany(o => o.Results).ToList(),
            SourceStatuses = outcomes.Select(o => o.Status).ToList(),
        };
    }

    public Task<BookSearchResult> GetBookDetails(string bookUrl)
    {
        var scraper = _scrapers.SingleOrDefault(s => s.SupportsUrl(bookUrl));

        if (scraper == default)
        {
            throw new Exception($"No scraper supports url {bookUrl}");
        }

        return GetBookDetailsFromScraper(scraper, bookUrl);
    }

    private static async Task<BookSearchResult> GetBookDetailsFromScraper(IScraper scraper, string bookUrl)
    {
        var result = await scraper.GetBookDetails(bookUrl);
        result.Source = scraper.SourceName;
        return result;
    }

    public IList<string> GetListOfScrapingServices()
    {
        return _scrapers.Select(x => x.SourceName).ToList();
    }

    public IList<SearchServiceInfo> GetSearchServiceInfo()
    {
        return _scrapers.Select(s =>
        {
            var enabled = !s.RequiresApiKey || s.IsApiKeyConfigured;
            string? disabledReason = !enabled ? "API key not configured" : null;
            return new SearchServiceInfo(s.SourceName, enabled, disabledReason);
        }).ToList();
    }
}
