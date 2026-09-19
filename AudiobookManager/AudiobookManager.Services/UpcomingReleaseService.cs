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
    private readonly ISeriesReconciliationProvider _seriesReconciliationProvider;
    private readonly IAuthorReconciliationProvider _authorReconciliationProvider;
    private readonly ISeriesReconciliationCache _seriesReconciliationCache;
    private readonly IEnumerable<IScraper> _scrapers;
    private readonly ILogger<UpcomingReleaseService> _logger;

    public UpcomingReleaseService(
        IPersonRepository personRepository,
        ISeriesRepository seriesRepository,
        IAuthorFollowRepository authorFollowRepository,
        ISeriesFollowRepository seriesFollowRepository,
        IUpcomingReleaseRepository upcomingReleaseRepository,
        ISeriesReconciliationProvider seriesReconciliationProvider,
        IAuthorReconciliationProvider authorReconciliationProvider,
        ISeriesReconciliationCache seriesReconciliationCache,
        IEnumerable<IScraper> scrapers,
        ILogger<UpcomingReleaseService> logger)
    {
        _personRepository = personRepository;
        _seriesRepository = seriesRepository;
        _authorFollowRepository = authorFollowRepository;
        _seriesFollowRepository = seriesFollowRepository;
        _upcomingReleaseRepository = upcomingReleaseRepository;
        _seriesReconciliationProvider = seriesReconciliationProvider;
        _authorReconciliationProvider = authorReconciliationProvider;
        _seriesReconciliationCache = seriesReconciliationCache;
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

    public async Task<(List<UpcomingReleaseItem> Items, int Total)> GetUpcomingReleasesAsync(
        long? personId, long? seriesId, int limit, int offset)
    {
        var items = await BuildMergedItemsAsync(personId, seriesId);
        var total = items.Count;
        var page = items.Skip(offset).Take(limit).ToList();
        return (page, total);
    }

    public Task<bool> RemoveUpcomingReleaseAsync(long id) => _upcomingReleaseRepository.DeleteAsync(id);

    public async Task DismissAuthorRosterUpcomingAsync(long personId, string title) =>
        await _personRepository.SetAuthorExpectedBookIgnoredAsync(personId, title, true);

    public async Task DismissSeriesRosterUpcomingAsync(string seriesName, string? position, string title)
    {
        await _seriesRepository.SetExpectedBookIgnoredAsync(seriesName, position, title, true);
        _seriesReconciliationCache.Invalidate(seriesName);
    }

    /// <summary>
    /// Unions the legacy scrape-and-store table with every followed-and-matched series'/author's
    /// roster entries classified <c>Upcoming</c>, de-duplicated by (scope, normalized title) so
    /// the same upcoming book does not appear twice just because both pipelines know about it -
    /// the roster-derived entry wins a duplicate, since it is the one that can actually be
    /// dismissed (see <see cref="UpcomingReleaseSource"/>). The candidate set is bounded by the
    /// number of followed-and-matched authors/series, which <see cref="RefreshUpcomingReleasesAsync"/>
    /// already treats as small enough to poll synchronously, so building the whole set in memory
    /// before sorting/paging is the same tradeoff that method already makes.
    ///
    /// The dedup key set also includes each series'/author's <c>Ignored</c> roster entries (not
    /// just <c>Upcoming</c>) for the same reason: once a roster-derived entry is dismissed, it
    /// must keep suppressing its legacy-table duplicate, or the same book reappears immediately
    /// as a fresh "Legacy" row the moment its "Upcoming" entry is ignored. An ignored entry
    /// contributes only to <c>rosterKeys</c>, never to <c>items</c> - it stays invisible, it just
    /// keeps suppressing the legacy duplicate.
    /// </summary>
    private async Task<List<UpcomingReleaseItem>> BuildMergedItemsAsync(long? personId, long? seriesId)
    {
        var legacy = await _upcomingReleaseRepository.GetAllAsync(personId, seriesId);
        var items = new List<UpcomingReleaseItem>(legacy.Count);
        var rosterKeys = new HashSet<(string Scope, string Title)>();

        if (seriesId is null)
        {
            var followedSeries = personId is null
                ? await _seriesFollowRepository.GetFollowedMatchedSeriesAsync()
                : new List<Series>();

            foreach (var series in followedSeries)
            {
                var reconciliation = await _seriesReconciliationProvider.GetReconciliationAsync(series.Name);
                foreach (var entry in reconciliation.Upcoming)
                {
                    var key = ($"series:{series.Id}", NormalizeTitleForCarryOver(entry.Title));
                    rosterKeys.Add(key);
                    items.Add(new UpcomingReleaseItem(
                        UpcomingReleaseSource.Roster, null, entry.Title, entry.ReleaseDate, entry.Year,
                        null, null, series.Id, series.Name, entry.Position, "roster", entry.SourceUrl, null));
                }

                // An ignored roster entry has no visible item, but must still suppress its legacy
                // duplicate - otherwise dismissing a roster-derived "upcoming" entry immediately
                // resurrects the same book as a fresh "Legacy" row. See UPCOMING_RELEASES_DESIGN.md.
                foreach (var entry in reconciliation.Ignored)
                {
                    rosterKeys.Add(($"series:{series.Id}", NormalizeTitleForCarryOver(entry.Title)));
                }
            }
        }
        else
        {
            var series = await _seriesRepository.GetByIdWithExpectedBooksAsync(seriesId.Value);
            if (series is not null && await _seriesFollowRepository.IsFollowedAsync(series.Id)
                && !string.IsNullOrEmpty(series.MatchedSourceId))
            {
                var reconciliation = await _seriesReconciliationProvider.GetReconciliationAsync(series.Name);
                foreach (var entry in reconciliation.Upcoming)
                {
                    rosterKeys.Add(($"series:{series.Id}", NormalizeTitleForCarryOver(entry.Title)));
                    items.Add(new UpcomingReleaseItem(
                        UpcomingReleaseSource.Roster, null, entry.Title, entry.ReleaseDate, entry.Year,
                        null, null, series.Id, series.Name, entry.Position, "roster", entry.SourceUrl, null));
                }

                foreach (var entry in reconciliation.Ignored)
                {
                    rosterKeys.Add(($"series:{series.Id}", NormalizeTitleForCarryOver(entry.Title)));
                }
            }
        }

        if (personId is null)
        {
            var followedAuthorsScope = seriesId is null
                ? await _authorFollowRepository.GetFollowedMatchedAuthorsAsync()
                : new List<Person>();

            foreach (var author in followedAuthorsScope)
            {
                var reconciliation = await _authorReconciliationProvider.GetReconciliationAsync(author.Id);
                foreach (var entry in reconciliation.Upcoming)
                {
                    rosterKeys.Add(($"author:{author.Id}", NormalizeTitleForCarryOver(entry.Title)));
                    items.Add(new UpcomingReleaseItem(
                        UpcomingReleaseSource.Roster, null, entry.Title, entry.ReleaseDate, entry.Year,
                        author.Id, author.Name, null, null, null, "roster", entry.SourceUrl, null));
                }

                foreach (var entry in reconciliation.Ignored)
                {
                    rosterKeys.Add(($"author:{author.Id}", NormalizeTitleForCarryOver(entry.Title)));
                }
            }
        }
        else
        {
            var person = await _personRepository.GetByIdAsync(personId.Value);
            if (person is not null && await _authorFollowRepository.IsFollowedAsync(person.Id)
                && !string.IsNullOrEmpty(person.HardcoverAuthorId))
            {
                var reconciliation = await _authorReconciliationProvider.GetReconciliationAsync(person.Id);
                foreach (var entry in reconciliation.Upcoming)
                {
                    rosterKeys.Add(($"author:{person.Id}", NormalizeTitleForCarryOver(entry.Title)));
                    items.Add(new UpcomingReleaseItem(
                        UpcomingReleaseSource.Roster, null, entry.Title, entry.ReleaseDate, entry.Year,
                        person.Id, person.Name, null, null, null, "roster", entry.SourceUrl, null));
                }

                foreach (var entry in reconciliation.Ignored)
                {
                    rosterKeys.Add(($"author:{person.Id}", NormalizeTitleForCarryOver(entry.Title)));
                }
            }
        }

        foreach (var r in legacy)
        {
            var scope = r.SeriesId is not null ? $"series:{r.SeriesId}" : r.PersonId is not null ? $"author:{r.PersonId}" : null;
            if (scope is not null && rosterKeys.Contains((scope, NormalizeTitleForCarryOver(r.Title))))
            {
                // The roster-derived entry for this same book already represents it.
                continue;
            }

            items.Add(new UpcomingReleaseItem(
                UpcomingReleaseSource.Legacy, r.Id, r.Title, r.ReleaseDate, r.ReleaseDate.Year,
                r.PersonId, r.Person?.Name, r.SeriesId, r.Series?.Name, r.SeriesPosition,
                r.SourceName, r.SourceUrl, r.ImageUrl));
        }

        return items
            .OrderBy(i => i.SortDate)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Source)
            .ThenBy(i => i.Id ?? long.MaxValue)
            .ToList();
    }

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

    public async Task RefreshAuthorRosterAsync(long personId)
    {
        var person = await _personRepository.GetByIdAsync(personId)
            ?? throw new KeyNotFoundException($"Author {personId} not found");

        if (string.IsNullOrEmpty(person.HardcoverAuthorId))
        {
            throw new KeyNotFoundException($"Author {personId} is not matched to a metadata source, so it cannot be refreshed.");
        }

        var scraper = AuthorLookupScraper
            ?? throw new ArgumentException("No author-capable scraper is available to refresh this author's roster.");

        await RefreshAuthorRosterCoreAsync(scraper, person);
    }

    public async Task<(int Processed, int Succeeded, int Failed, string? StopReason)> RefreshAllAuthorRostersAsync()
    {
        var scraper = AuthorLookupScraper;
        if (scraper is null)
        {
            return (0, 0, 0, "No author-capable metadata source is configured.");
        }

        // Concurrency between this sweep, the single-author refresh, and a second direct call to
        // either is gated by BrowseController's shared static _refreshLock (mirrors
        // SeriesController's _refreshLock over RefreshSeries/RefreshAllSeries) - both endpoints
        // that reach RefreshAuthorRosterCoreAsync take it before calling into this service, so
        // two callers can never both read the ignore set and then both replace the same author's
        // roster. See the concurrency note on RefreshAuthorRosterCoreAsync.
        var authors = await _personRepository.GetMatchedAuthorsAsync();
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        string? stopReason = null;

        foreach (var author in authors)
        {
            processed++;
            try
            {
                await RefreshAuthorRosterCoreAsync(scraper, author);
                succeeded++;
            }
            catch (HardcoverDailyLimitExceededException ex)
            {
                processed--;
                stopReason = "Stopped early: the daily request budget for this source ran out.";
                _logger.LogWarning(ex, "Stopping author-roster refresh after {Processed}/{Total} authors: {Message}",
                    processed, authors.Count, ex.Message);
                break;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Failed to refresh the standalone-books roster for author {PersonId}", author.Id);
            }
        }

        return (processed, succeeded, failed, stopReason);
    }

    /// <summary>
    /// The one-author workload shared by the single and bulk refresh: fetch the author's full
    /// bibliography, drop anything that belongs to a series (already rostered/refreshed through
    /// that series' own roster - see the design note this feature ships with), replace the
    /// stored roster wholesale (carrying ignore decisions across for entries recognisably the
    /// same book, by normalized title - the author roster has no position to match on), and
    /// stamp LastRefreshedAt.
    ///
    /// This method is reachable concurrently from two controller endpoints
    /// (<c>BrowseController.RefreshAuthor</c>/<c>RefreshAllAuthors</c>) and does not itself gate
    /// against that - the concurrency invariant is enforced by the caller. Both endpoints take
    /// the SAME static <c>BrowseController._refreshLock</c> (mirroring
    /// <c>SeriesController._refreshLock</c> over <c>RefreshSeries</c>/<c>RefreshAllSeries</c>)
    /// before reaching this method, so a single-author refresh can never race the bulk sweep (or
    /// another single-author refresh) into a read-then-delete-then-insert on the same author's
    /// roster, and a dismissal made between the ignore-set read and the replace can never be lost
    /// to a concurrent re-insert of the stale flag.
    /// </summary>
    private async Task RefreshAuthorRosterCoreAsync(IScraper scraper, Person person)
    {
        var books = await scraper.GetAuthorBooks(person.HardcoverAuthorId!);
        var standalone = books.Where(b => !b.HasSeries).ToList();

        var (existing, _) = await _personRepository.GetByIdWithExpectedBooksBoundedAsync(
            person.Id, AuthorReconciliationProvider.MaxReconciliationRosterEntries);
        var previouslyIgnoredTitles = new HashSet<string>(
            (existing?.ExpectedBooks ?? new List<AuthorExpectedBook>())
                .Where(b => b.IsIgnored)
                .Select(b => NormalizeTitleForCarryOver(b.Title)),
            StringComparer.Ordinal);

        var newExpected = standalone.Select(b => new AuthorExpectedBook
        {
            Title = b.Title,
            Year = b.Year,
            ReleaseDate = b.ReleaseDate,
            SourceUrl = b.SourceUrl,
            IsIgnored = previouslyIgnoredTitles.Contains(NormalizeTitleForCarryOver(b.Title)),
        }).ToList();

        await _personRepository.ReplaceAuthorExpectedBooksAsync(person.Id, newExpected);
        await _personRepository.SetLastRefreshedAtAsync(person.Id, DateTime.UtcNow);
    }

    private static string NormalizeTitleForCarryOver(string title) => title.Trim().ToLowerInvariant();

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
