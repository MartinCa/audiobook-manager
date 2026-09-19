using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Scraping.Scrapers;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

public class UpcomingReleaseService : IUpcomingReleaseService
{
    private readonly IPersonRepository _personRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly IAuthorFollowRepository _authorFollowRepository;
    private readonly ISeriesFollowRepository _seriesFollowRepository;
    private readonly IUpcomingReleaseRepository _upcomingReleaseRepository;
    private readonly IEnumerable<IScraper> _scrapers;
    private readonly ILogger<UpcomingReleaseService> _logger;

    public UpcomingReleaseService(
        IPersonRepository personRepository,
        ISeriesRepository seriesRepository,
        IAuthorFollowRepository authorFollowRepository,
        ISeriesFollowRepository seriesFollowRepository,
        IUpcomingReleaseRepository upcomingReleaseRepository,
        IEnumerable<IScraper> scrapers,
        ILogger<UpcomingReleaseService> logger)
    {
        _personRepository = personRepository;
        _seriesRepository = seriesRepository;
        _authorFollowRepository = authorFollowRepository;
        _seriesFollowRepository = seriesFollowRepository;
        _upcomingReleaseRepository = upcomingReleaseRepository;
        _scrapers = scrapers;
        _logger = logger;
    }

    /// <summary>
    /// The only source with author lookup support today - "for now just support Hardcover", per
    /// the feature scope. A second source would add its own IScraper implementation and this
    /// would become a lookup by source name like <see cref="ISeriesService"/>'s scraper
    /// selection, instead of a single field.
    /// </summary>
    private IScraper? AuthorLookupScraper =>
        _scrapers.FirstOrDefault(s => s.SupportsAuthorLookup && (!s.RequiresApiKey || s.IsApiKeyConfigured));

    public Task<bool> IsAuthorFollowedAsync(long personId) => _authorFollowRepository.IsFollowedAsync(personId);

    public async Task FollowAuthorAsync(long personId)
    {
        var person = await _personRepository.GetByIdAsync(personId)
            ?? throw new KeyNotFoundException($"Author {personId} not found");

        await _authorFollowRepository.FollowAsync(person.Id);
    }

    public Task UnfollowAuthorAsync(long personId) => _authorFollowRepository.UnfollowAsync(personId);

    public async Task<List<AuthorSearchResult>> SearchAuthorMatchCandidatesAsync(string query)
    {
        var scraper = AuthorLookupScraper;
        if (scraper is null || string.IsNullOrWhiteSpace(query))
        {
            return new List<AuthorSearchResult>();
        }

        var results = await scraper.SearchAuthors(query.Trim());
        foreach (var result in results)
        {
            result.Source = scraper.SourceName;
        }

        return results.ToList();
    }

    public async Task MatchAuthorAsync(long personId, string sourceId, string sourceName, string? sourceUrl)
    {
        _ = await _personRepository.GetByIdAsync(personId)
            ?? throw new KeyNotFoundException($"Author {personId} not found");

        await _personRepository.SetHardcoverMatchAsync(personId, sourceId, sourceName, sourceUrl);
    }

    public async Task UnmatchAuthorAsync(long personId)
    {
        _ = await _personRepository.GetByIdAsync(personId)
            ?? throw new KeyNotFoundException($"Author {personId} not found");

        await _personRepository.SetHardcoverMatchAsync(personId, null, null, null);
    }

    public Task<bool> IsSeriesFollowedAsync(long seriesId) => _seriesFollowRepository.IsFollowedAsync(seriesId);

    public async Task<bool> IsSeriesFollowedByNameAsync(string seriesName)
    {
        var series = await _seriesRepository.GetByNameAsync(seriesName);
        return series is not null && await _seriesFollowRepository.IsFollowedAsync(series.Id);
    }

    public async Task FollowSeriesAsync(string seriesName)
    {
        if (string.IsNullOrWhiteSpace(seriesName))
        {
            throw new ArgumentException("Series name is required.", nameof(seriesName));
        }

        var (series, _) = await _seriesRepository.GetOrCreateByNameAsync(seriesName.Trim());
        await _seriesFollowRepository.FollowAsync(series.Id);
    }

    public async Task UnfollowSeriesAsync(string seriesName)
    {
        var series = await _seriesRepository.GetByNameAsync(seriesName);
        if (series is null)
        {
            return;
        }

        await _seriesFollowRepository.UnfollowAsync(series.Id);
    }

    public Task<(List<UpcomingRelease> Items, int Total)> GetUpcomingReleasesAsync(
        long? personId, long? seriesId, int limit, int offset) =>
        _upcomingReleaseRepository.GetPagedAsync(personId, seriesId, limit, offset);

    public Task<bool> RemoveUpcomingReleaseAsync(long id) => _upcomingReleaseRepository.DeleteAsync(id);

    public async Task RefreshUpcomingReleasesAsync()
    {
        var scraper = AuthorLookupScraper;
        if (scraper is null)
        {
            return;
        }

        var authors = await _authorFollowRepository.GetFollowedMatchedAuthorsAsync();
        foreach (var author in authors)
        {
            try
            {
                await RefreshAuthorAsync(scraper, author);
            }
            catch (HardcoverDailyLimitExceededException ex)
            {
                // Nothing left in today's budget will succeed either - stop this cycle rather
                // than fail through every remaining author/series. The periodic worker tries
                // again on its next tick, by which point the daily window has likely rolled over.
                _logger.LogWarning(ex, "Stopping upcoming-releases refresh: {Message}", ex.Message);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh upcoming releases for followed author {PersonId}", author.Id);
            }
        }

        var seriesList = await _seriesFollowRepository.GetFollowedMatchedSeriesAsync();
        foreach (var series in seriesList)
        {
            try
            {
                await RefreshSeriesAsync(scraper, series);
            }
            catch (HardcoverDailyLimitExceededException ex)
            {
                _logger.LogWarning(ex, "Stopping upcoming-releases refresh: {Message}", ex.Message);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh upcoming releases for followed series {SeriesId}", series.Id);
            }
        }
    }

    private async Task RefreshAuthorAsync(IScraper scraper, Person author)
    {
        var releases = await scraper.GetAuthorUpcomingReleases(author.HardcoverAuthorId!);
        foreach (var release in releases)
        {
            long? seriesId = null;
            if (!string.IsNullOrEmpty(release.SeriesSourceId))
            {
                // Keyed on the exact (source name, source id) pair, not the local series name -
                // the local catalog row's name and the source's own series name are not
                // guaranteed to agree (the user may have renamed it, or adopted a different
                // spelling), so a name-based lookup would miss a followed-but-differently-named
                // series entirely.
                var matchedSeries = await _seriesRepository.GetByMatchedSourceIdAsync(scraper.SourceName, release.SeriesSourceId);
                if (matchedSeries is not null)
                {
                    seriesId = matchedSeries.Id;
                }
            }

            await _upcomingReleaseRepository.UpsertAsync(ToEntity(scraper, release, personId: author.Id, seriesId: seriesId));
        }
    }

    private async Task RefreshSeriesAsync(IScraper scraper, Series series)
    {
        var releases = await scraper.GetSeriesUpcomingReleases(series.MatchedSourceId!);
        foreach (var release in releases)
        {
            await _upcomingReleaseRepository.UpsertAsync(ToEntity(scraper, release, personId: null, seriesId: series.Id));
        }
    }

    private static UpcomingRelease ToEntity(IScraper scraper, UpcomingReleaseResult release, long? personId, long? seriesId) => new()
    {
        Title = release.Title,
        ReleaseDate = release.ReleaseDate,
        PersonId = personId,
        SeriesId = seriesId,
        SeriesPosition = release.SeriesPosition,
        SourceName = scraper.SourceName,
        SourceBookId = release.SourceBookId,
        SourceUrl = release.SourceUrl,
        ImageUrl = release.ImageUrl,
        DiscoveredAt = DateTime.UtcNow,
    };
}
