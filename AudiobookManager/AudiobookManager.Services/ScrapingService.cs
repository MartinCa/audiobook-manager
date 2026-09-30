using AudiobookManager.Domain;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.Scrapers;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;
public class ScrapingService : IScrapingService
{
    private readonly IEnumerable<IScraper> _scrapers;
    private readonly IQualifierIndicatorService _qualifierIndicators;
    private readonly ILogger<ScrapingService> _logger;

    public ScrapingService(
        IEnumerable<IScraper> scrapers,
        IQualifierIndicatorService qualifierIndicators,
        ILogger<ScrapingService> logger)
    {
        _scrapers = scrapers;
        _qualifierIndicators = qualifierIndicators;
        _logger = logger;
    }

    /// <summary>
    /// Moves the qualifiers a source spells out in a title (<c>(Abridged)</c>,
    /// <c>[Dramatized Adaptation]</c>, per the configured rules) off <see cref="MetadataSearchResult.BookName"/>
    /// and into <see cref="MetadataSearchResult.Qualifiers"/>, merged with any qualifier the scraper
    /// itself read from structured data (Audible's "Abridged Audiobook" format). Done here, once,
    /// so every consumer - the search dialog, a single refresh, the bulk refresh and online
    /// matching - sees the same clean name.
    /// </summary>
    private void ApplyQualifiers(MetadataSearchResult result, IReadOnlyList<QualifierIndicatorRule> rules)
    {
        var (title, fromTitle) = QualifierIndicators.Extract(result.BookName, result.Source, rules);
        if (!string.IsNullOrWhiteSpace(title))
        {
            result.BookName = title;
        }

        result.Qualifiers = BookQualifiers.Normalize((result.Qualifiers ?? new List<string>()).Concat(fromTitle));
    }

    public async Task<IList<MetadataSearchResult>> Search(string sourceName, string searchTerm)
    {
        var scraper = _scrapers.SingleOrDefault(s => s.IsSource(sourceName));

        if (scraper == default)
        {
            throw new Exception($"No scraper for source {sourceName}");
        }

        var results = await scraper.Search(searchTerm);
        var rules = await _qualifierIndicators.GetRulesAsync();
        foreach (var result in results)
        {
            result.Source = scraper.SourceName;
            ApplyQualifiers(result, rules);
        }

        return results;
    }

    public async Task<MetadataMultiSourceSearchResult> SearchMultiple(IEnumerable<string> sourceNames, string searchTerm)
    {
        var scrapers = _scrapers.Where(s => sourceNames.Any(s.IsSource)).ToList();
        var rules = await _qualifierIndicators.GetRulesAsync();

        var searchTasks = scrapers.Select(async scraper =>
        {
            try
            {
                var results = await scraper.Search(searchTerm);
                foreach (var result in results)
                {
                    result.Source = scraper.SourceName;
                    ApplyQualifiers(result, rules);
                }

                return (Status: new MetadataSourceSearchStatus
                {
                    Source = scraper.SourceName,
                    Success = true,
                    ResultCount = results.Count,
                }, Results: results);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search failed for source {Source}", scraper.SourceName);

                return (Status: new MetadataSourceSearchStatus
                {
                    Source = scraper.SourceName,
                    Success = false,
                    ResultCount = 0,
                    Error = ex.Message,
                }, Results: (IList<MetadataSearchResult>)new List<MetadataSearchResult>());
            }
        });

        var outcomes = await Task.WhenAll(searchTasks);

        return new MetadataMultiSourceSearchResult
        {
            Results = outcomes.SelectMany(o => o.Results).ToList(),
            SourceStatuses = outcomes.Select(o => o.Status).ToList(),
        };
    }

    public Task<MetadataSearchResult> GetBookDetails(string bookUrl)
    {
        var scraper = _scrapers.SingleOrDefault(s => s.SupportsUrl(bookUrl));

        if (scraper == default)
        {
            // Caller-supplied input (a pasted URL none of the registered scrapers recognize), not
            // a server failure - an ArgumentException so the controller can relay this message as
            // a 400 rather than an opaque 500 (see ProblemResults.cs).
            throw new ArgumentException($"No configured metadata source supports the URL '{bookUrl}'.");
        }

        if (scraper.RequiresApiKey && !scraper.IsApiKeyConfigured)
        {
            // The URL matches this source, but GetSearchServiceInfo() already reports it as
            // disabled (the source picker shows the same "API key not configured" reason) - tell
            // the caller that instead of letting the scraper itself fail with something opaque.
            throw new ArgumentException($"{scraper.SourceName} supports this URL, but its API key is not configured.");
        }

        return GetBookDetailsFromScraper(scraper, bookUrl);
    }

    private async Task<MetadataSearchResult> GetBookDetailsFromScraper(IScraper scraper, string bookUrl)
    {
        var result = await scraper.GetBookDetails(bookUrl);
        result.Source = scraper.SourceName;
        ApplyQualifiers(result, await _qualifierIndicators.GetRulesAsync());
        return result;
    }

    public IList<string> GetListOfScrapingServices()
    {
        return _scrapers.Select(x => x.SourceName).ToList();
    }

    public IList<MetadataSearchServiceInfo> GetSearchServiceInfo()
    {
        return _scrapers.Select(s =>
        {
            var enabled = !s.RequiresApiKey || s.IsApiKeyConfigured;
            string? disabledReason = !enabled ? "API key not configured" : null;
            return new MetadataSearchServiceInfo(s.SourceName, enabled, disabledReason);
        }).ToList();
    }
}
