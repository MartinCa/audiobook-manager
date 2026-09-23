using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Scraping;
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
    private readonly IExpectedBookRepository _expectedBookRepository;
    private readonly ISeriesReconciliationProvider _seriesReconciliationProvider;
    private readonly IAuthorReconciliationProvider _authorReconciliationProvider;
    private readonly ISeriesReconciliationCache _seriesReconciliationCache;
    private readonly IAuthorConsistencyIssueRepository _authorConsistencyIssueRepository;
    private readonly IEnumerable<IScraper> _scrapers;
    private readonly ILogger<UpcomingReleaseService> _logger;

    public UpcomingReleaseService(
        IPersonRepository personRepository,
        ISeriesRepository seriesRepository,
        IAuthorFollowRepository authorFollowRepository,
        ISeriesFollowRepository seriesFollowRepository,
        IUpcomingReleaseRepository upcomingReleaseRepository,
        IExpectedBookRepository expectedBookRepository,
        ISeriesReconciliationProvider seriesReconciliationProvider,
        IAuthorReconciliationProvider authorReconciliationProvider,
        ISeriesReconciliationCache seriesReconciliationCache,
        IAuthorConsistencyIssueRepository authorConsistencyIssueRepository,
        IEnumerable<IScraper> scrapers,
        ILogger<UpcomingReleaseService> logger)
    {
        _personRepository = personRepository;
        _seriesRepository = seriesRepository;
        _authorFollowRepository = authorFollowRepository;
        _seriesFollowRepository = seriesFollowRepository;
        _upcomingReleaseRepository = upcomingReleaseRepository;
        _expectedBookRepository = expectedBookRepository;
        _seriesReconciliationProvider = seriesReconciliationProvider;
        _authorReconciliationProvider = authorReconciliationProvider;
        _seriesReconciliationCache = seriesReconciliationCache;
        _authorConsistencyIssueRepository = authorConsistencyIssueRepository;
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

    /// <summary>
    /// Persists the author's match. The immediate roster refresh that follows lives in the
    /// controller under the shared expected-book write gate (<see cref="IExpectedBookWriteGate"/>,
    /// in <c>BrowseController.MatchAuthor</c>), because this service does not own that gate - the
    /// match must persist even when the refresh fails transiently, and the periodic sweep then
    /// picks the roster up on its next tick.
    /// </summary>
    public async Task MatchAuthorAsync(long personId, string sourceId, string sourceName, string? sourceUrl)
    {
        _ = await _personRepository.GetByIdAsync(personId)
            ?? throw new KeyNotFoundException($"Author {personId} not found");

        await _personRepository.SetAuthorMatchAsync(personId, sourceName, sourceId, sourceUrl);
    }

    public async Task UnmatchAuthorAsync(long personId)
    {
        _ = await _personRepository.GetByIdAsync(personId)
            ?? throw new KeyNotFoundException($"Author {personId} not found");

        await _personRepository.SetAuthorMatchAsync(personId, null, null, null);
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

    /// <summary>
    /// Dismisses a roster-derived upcoming release for a followed author by setting
    /// <c>IsIgnored</c> on the shared unified expected-book row (the same row a series refresh
    /// or another author may link to), so the book drops out of every scope's view at once -
    /// the global-ignore behavior the unified roster gives for free. A title-addressed dismissal
    /// is the compatibility fallback; new callers should use
    /// <see cref="DismissAuthorRosterUpcomingByIdAsync"/> since one author can carry two
    /// rows with the same title. Also invalidates the cache of any local series the row is
    /// linked to, whose view changes with the flag.
    /// </summary>
    public async Task DismissAuthorRosterUpcomingAsync(long personId, string title) =>
        await InvalidateSeriesOfIgnoredRowAsync(
            await _expectedBookRepository.SetExpectedBookIgnoredByPersonAsync(
                personId, title, true, AuthorReconciliationProvider.MaxReconciliationRosterEntries));

    /// <summary>Un-dismissal counterpart of <see cref="DismissAuthorRosterUpcomingAsync"/>.</summary>
    public async Task RestoreAuthorRosterUpcomingAsync(long personId, string title) =>
        await InvalidateSeriesOfIgnoredRowAsync(
            await _expectedBookRepository.SetExpectedBookIgnoredByPersonAsync(
                personId, title, false, AuthorReconciliationProvider.MaxReconciliationRosterEntries));

    /// <inheritdoc cref="IUpcomingReleaseService.DismissAuthorRosterUpcomingByIdAsync"/>
    public async Task DismissAuthorRosterUpcomingByIdAsync(long expectedBookId) =>
        await SetIgnoredByIdAsync(expectedBookId, true);

    /// <inheritdoc cref="IUpcomingReleaseService.RestoreAuthorRosterUpcomingByIdAsync"/>
    public async Task RestoreAuthorRosterUpcomingByIdAsync(long expectedBookId) =>
        await SetIgnoredByIdAsync(expectedBookId, false);

    /// <inheritdoc cref="IUpcomingReleaseService.DismissRosterUpcomingBySourceAsync"/>
    public async Task DismissRosterUpcomingBySourceAsync(string sourceName, string sourceBookId)
    {
        var book = await _expectedBookRepository.GetBySourceAsync(sourceName, sourceBookId)
            ?? throw new KeyNotFoundException($"Expected book (source '{sourceName}', id '{sourceBookId}') not found");

        await _expectedBookRepository.SetIgnoredByIdAsync(book.Id, true);
        await InvalidateSeriesOfIgnoredRowAsync(book.SeriesId);
    }

    private async Task SetIgnoredByIdAsync(long expectedBookId, bool ignored)
    {
        var book = await _expectedBookRepository.GetByIdAsync(expectedBookId)
            ?? throw new KeyNotFoundException($"Expected book {expectedBookId} not found");

        await _expectedBookRepository.SetIgnoredByIdAsync(expectedBookId, ignored);
        await InvalidateSeriesOfIgnoredRowAsync(book.SeriesId);
    }

    /// <summary>
    /// Drops the <see cref="ISeriesReconciliationCache"/> entry of the local series a changed
    /// ignore flag belongs to (a no-op for a standalone row): a shared row's series view renders
    /// from that cache, and a dismissal/restore through the author scope must not leave it stale
    /// for the cache's whole TTL.
    /// </summary>
    private async Task InvalidateSeriesOfIgnoredRowAsync(long? seriesId)
    {
        if (seriesId is not long id)
        {
            return;
        }

        var seriesName = await _seriesRepository.GetNameByIdAsync(id);
        if (seriesName is not null)
        {
            _seriesReconciliationCache.Invalidate(seriesName);
        }
    }

    public async Task DismissSeriesRosterUpcomingAsync(string seriesName, string? position, string title)
    {
        await _seriesRepository.SetExpectedBookIgnoredAsync(
            seriesName, position, title, true, SeriesReconciliationProvider.MaxReconciliationRosterEntries);
        _seriesReconciliationCache.Invalidate(seriesName);
    }

    /// <summary>
    /// Unions the legacy scrape-and-store table with every followed-and-matched series'/author's
    /// roster entries classified <c>Upcoming</c>, de-duplicated by the roster entry's source
    /// identity (<see cref="ExpectedBook.SourceName"/> + <see cref="ExpectedBook.SourceBookId"/> -
    /// the unified row's dedup identity, so the same book discovered by an author refresh and by
    /// the series it belongs to is ONE row) with a scope + normalized-title fallback for rows
    /// without a source id. The candidate set is bounded by the number of followed-and-matched
    /// authors/series, which <see cref="RefreshUpcomingReleasesAsync"/> already treats as small
    /// enough to poll synchronously, so building the whole set in memory before sorting/paging is
    /// the same tradeoff that method already makes.
    ///
    /// A followed author's upcoming book always appears, series books included, whether or not
    /// its series is followed - the author's roster now spans the whole bibliography, so the
    /// author scope carries those books itself.
    ///
    /// The dedup key set also includes each series'/author's <c>Ignored</c> roster entries (not
    /// just <c>Upcoming</c>) for the same reason: once a roster-derived entry is dismissed, it
    /// must keep suppressing its legacy-table duplicate, or the same book reappears immediately
    /// as a fresh "Legacy" row the moment its "Upcoming" entry is ignored. An ignored entry
    /// contributes only to the key sets, never to <c>items</c> - it stays invisible, it just
    /// keeps suppressing the legacy duplicate. Dismissing the shared row (from either scope)
    /// hides it from both, which is why one key set covers both scopes.
    /// </summary>
    private async Task<List<UpcomingReleaseItem>> BuildMergedItemsAsync(long? personId, long? seriesId)
    {
        var legacy = await _upcomingReleaseRepository.GetAllAsync(personId, seriesId);

        var rosterItems = new List<UpcomingReleaseItem>();
        var rosterSourceKeys = new HashSet<(string SourceName, string SourceBookId)>();
        var rosterScopeTitleKeys = new HashSet<(string Scope, string Title)>();

        if (seriesId is null)
        {
            var followedSeries = personId is null
                ? await _seriesFollowRepository.GetFollowedMatchedSeriesAsync()
                : new List<Series>();

            foreach (var series in followedSeries)
            {
                await CollectSeriesRoster(series, rosterItems, rosterSourceKeys, rosterScopeTitleKeys);
            }
        }
        else
        {
            // Scoped call (the series detail page): show the roster regardless of follow status -
            // only a matched source is required, so there is a roster to reconcile against. See
            // UPCOMING_RELEASES_DESIGN.md: "The per-series/per-author detail page shows
            // missing/upcoming regardless of follow status." The unscoped branch above keeps the
            // followed-and-matched requirement for the global page.
            var series = await _seriesRepository.GetByIdWithExpectedBooksAsync(seriesId.Value);
            if (series is not null && !string.IsNullOrEmpty(series.MatchedSourceId))
            {
                await CollectSeriesRoster(series, rosterItems, rosterSourceKeys, rosterScopeTitleKeys);
            }
        }

        if (personId is null)
        {
            var followedAuthorsScope = seriesId is null
                ? await _authorFollowRepository.GetFollowedMatchedAuthorsAsync()
                : new List<Person>();

            foreach (var author in followedAuthorsScope)
            {
                await CollectAuthorRoster(author, rosterItems, rosterSourceKeys, rosterScopeTitleKeys);
            }
        }
        else
        {
            // Scoped call (the author detail page): same rationale as the series branch above -
            // matched is sufficient, followed is not required.
            var person = await _personRepository.GetByIdAsync(personId.Value);
            if (person is not null && !string.IsNullOrEmpty(person.MatchedSourceId))
            {
                await CollectAuthorRoster(person, rosterItems, rosterSourceKeys, rosterScopeTitleKeys);
            }
        }

        var items = new List<UpcomingReleaseItem>(legacy.Count + rosterItems.Count);
        var dedupedRoster = DedupeRosterItems(rosterItems);
        foreach (var item in dedupedRoster)
        {
            // The merged item's keys are the union of both scopes' keys, so its legacy duplicate
            // is suppressed whichever scope the legacy row was recorded under.
            AddRosterDedupKeys(item, rosterSourceKeys, rosterScopeTitleKeys);
        }

        items.AddRange(dedupedRoster);

        foreach (var r in legacy)
        {
            var scope = r.SeriesId is not null ? $"series:{r.SeriesId}" : r.PersonId is not null ? $"author:{r.PersonId}" : null;
            var suppressed = (r.SourceBookId is not null && rosterSourceKeys.Contains((r.SourceName, r.SourceBookId!)))
                || (scope is not null && rosterScopeTitleKeys.Contains((scope, NormalizeTitleForCarryOver(r.Title))));

            if (suppressed)
            {
                // The roster-derived entry for this same book already represents it.
                continue;
            }

            items.Add(new UpcomingReleaseItem(
                UpcomingReleaseSource.Legacy, r.Id, r.Title, r.ReleaseDate, r.ReleaseDate.Year,
                r.PersonId, r.Person?.Name, r.SeriesId, r.Series?.Name, r.SeriesPosition,
                r.SourceName, r.SourceUrl, r.ImageUrl, r.SourceBookId));
        }

        return items
            .OrderBy(i => i.SortDate)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Source)
            .ThenBy(i => i.Id ?? long.MaxValue)
            .ToList();
    }

    private async Task CollectSeriesRoster(
        Series series,
        List<UpcomingReleaseItem> rosterItems,
        HashSet<(string SourceName, string SourceBookId)> sourceKeys,
        HashSet<(string Scope, string Title)> scopeTitleKeys)
    {
        var reconciliation = await _seriesReconciliationProvider.GetReconciliationAsync(series.Name);
        foreach (var entry in reconciliation.Upcoming)
        {
            rosterItems.Add(new UpcomingReleaseItem(
                UpcomingReleaseSource.Roster, null, entry.Title, entry.ReleaseDate, entry.Year,
                null, null, series.Id, series.Name, entry.Position,
                entry.SourceName, entry.SourceUrl, entry.ImageUrl, entry.SourceBookId, entry.Id));
        }

        AddIgnoredDedupKeys($"series:{series.Id}", reconciliation.Ignored.Select(i => (i.SourceName, i.SourceBookId, i.Title)).ToList(),
            sourceKeys, scopeTitleKeys);
    }

    private async Task CollectAuthorRoster(
        Person author,
        List<UpcomingReleaseItem> rosterItems,
        HashSet<(string SourceName, string SourceBookId)> sourceKeys,
        HashSet<(string Scope, string Title)> scopeTitleKeys)
    {
        // The upcoming view only classifies the roster; the missing-series groups the author
        // detail renders are not needed here (and must not fail this page over a pathological
        // group set - see IAuthorReconciliationProvider.GetReconciliationAsync).
        var reconciliation = await _authorReconciliationProvider.GetReconciliationAsync(
            author.Id, includeMissingSeries: false);
        foreach (var entry in reconciliation.Upcoming)
        {
            rosterItems.Add(new UpcomingReleaseItem(
                UpcomingReleaseSource.Roster, null, entry.Title, entry.ReleaseDate, entry.Year,
                author.Id, author.Name, entry.SeriesId, entry.SeriesName, entry.Position,
                entry.SourceName, entry.SourceUrl, entry.ImageUrl, entry.SourceBookId, entry.Id));
        }

        AddIgnoredDedupKeys($"author:{author.Id}", reconciliation.Ignored.Select(i => (i.SourceName, i.SourceBookId, i.Title)).ToList(),
            sourceKeys, scopeTitleKeys);
    }

    private static void AddIgnoredDedupKeys(
        string scope,
        IReadOnlyList<(string? SourceName, string? SourceBookId, string Title)> ignored,
        HashSet<(string SourceName, string SourceBookId)> sourceKeys,
        HashSet<(string Scope, string Title)> scopeTitleKeys)
    {
        foreach (var entry in ignored)
        {
            if (!string.IsNullOrEmpty(entry.SourceBookId))
            {
                sourceKeys.Add((entry.SourceName ?? string.Empty, entry.SourceBookId!));
            }

            scopeTitleKeys.Add((scope, NormalizeTitleForCarryOver(entry.Title)));
        }
    }

    private static void AddRosterDedupKeys(
        UpcomingReleaseItem item,
        HashSet<(string SourceName, string SourceBookId)> sourceKeys,
        HashSet<(string Scope, string Title)> scopeTitleKeys)
    {
        if (!string.IsNullOrEmpty(item.SourceBookId))
        {
            sourceKeys.Add((item.SourceName ?? string.Empty, item.SourceBookId!));
        }

        var title = NormalizeTitleForCarryOver(item.Title);
        if (item.SeriesId is not null)
        {
            scopeTitleKeys.Add(($"series:{item.SeriesId}", title));
        }

        if (item.AuthorId is not null)
        {
            scopeTitleKeys.Add(($"author:{item.AuthorId}", title));
        }
    }

    /// <summary>
    /// Collapses roster-derived items that denote the same source book. The primary key is the
    /// source identity (source name + source book id - the unified expected-book row's dedup
    /// identity); an entry without one falls back to scope + normalized title, the same key the
    /// legacy rows dedup on. When the same identity surfaces through both an author and a series
    /// scope, one item is emitted that prefers the series-linked representation (it carries the
    /// position/series metadata the series view renders) while keeping the author id so an
    /// author-scoped query and the author-routed dismissal still work on the shared book. Two
    /// author-scoped entries for the same identity (a coauthored book followed through both
    /// authors) keep the first one.
    /// </summary>
    private static List<UpcomingReleaseItem> DedupeRosterItems(List<UpcomingReleaseItem> rosterItems)
    {
        if (rosterItems.Count <= 1)
        {
            return rosterItems;
        }

        var bySourceIdentity = new Dictionary<(string SourceName, string SourceBookId), List<UpcomingReleaseItem>>();
        var kept = new List<UpcomingReleaseItem>(rosterItems.Count);
        var identityLessScopeKeys = new HashSet<(string Scope, string Title)>();

        foreach (var item in rosterItems)
        {
            if (!string.IsNullOrEmpty(item.SourceBookId))
            {
                var sourceKey = (item.SourceName ?? string.Empty, item.SourceBookId!);
                if (!bySourceIdentity.TryGetValue(sourceKey, out var identityGroup))
                {
                    identityGroup = new List<UpcomingReleaseItem>();
                    bySourceIdentity[sourceKey] = identityGroup;
                }

                identityGroup.Add(item);
                continue;
            }

            var scope = item.SeriesId is not null ? $"series:{item.SeriesId}" : item.AuthorId is not null ? $"author:{item.AuthorId}" : null;
            if (scope is null)
            {
                kept.Add(item);
                continue;
            }

            var scopeKey = (scope, NormalizeTitleForCarryOver(item.Title));
            if (identityLessScopeKeys.Contains(scopeKey))
            {
                continue;
            }

            identityLessScopeKeys.Add(scopeKey);
            kept.Add(item);
        }

        foreach (var group in bySourceIdentity.Values)
        {
            kept.Add(MergeRosterGroup(group));
        }

        return kept;
    }

    private static UpcomingReleaseItem MergeRosterGroup(List<UpcomingReleaseItem> group)
    {
        if (group.Count == 1)
        {
            return group.Single();
        }

        var seriesEntry = group.FirstOrDefault(i => i.SeriesId is not null);
        var authorEntry = group.FirstOrDefault(i => i.AuthorId is not null);
        if (seriesEntry is not null)
        {
            return new UpcomingReleaseItem(
                UpcomingReleaseSource.Roster, null,
                seriesEntry.Title, seriesEntry.ReleaseDate ?? authorEntry?.ReleaseDate, seriesEntry.Year ?? authorEntry?.Year,
                authorEntry?.AuthorId ?? seriesEntry.AuthorId, authorEntry?.AuthorName ?? seriesEntry.AuthorName,
                seriesEntry.SeriesId, seriesEntry.SeriesName, seriesEntry.SeriesPosition,
                seriesEntry.SourceName, seriesEntry.SourceUrl ?? authorEntry?.SourceUrl, seriesEntry.ImageUrl ?? authorEntry?.ImageUrl,
                seriesEntry.SourceBookId,
                seriesEntry.ExpectedBookId ?? authorEntry?.ExpectedBookId);
        }

        return group.First();
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
        var releases = await scraper.GetAuthorUpcomingReleases(author.MatchedSourceId!);
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

        if (string.IsNullOrEmpty(person.MatchedSourceId))
        {
            throw new KeyNotFoundException($"Author {personId} is not matched to a metadata source, so it cannot be refreshed.");
        }

        var scraper = AuthorLookupScraper
            ?? throw new ArgumentException("No author-capable scraper is available to refresh this author's roster.");

        await RefreshAuthorRosterTrackedAsync(scraper, person);
    }

    public async Task<(int Processed, int Succeeded, int Failed, string? StopReason)> RefreshAllAuthorRostersAsync()
    {
        var scraper = AuthorLookupScraper;
        if (scraper is null)
        {
            return (0, 0, 0, "No author-capable metadata source is configured.");
        }

        // Concurrency between this sweep, the single-author refresh, and the refresh triggered by
        // matching an author is gated by BrowseController's shared <see cref="IExpectedBookWriteGate"/>
        // (the same process-wide gate every series-side roster mutation holds) - every endpoint
        // that reaches RefreshAuthorRosterCoreAsync takes it before calling into this service, so
        // two callers can never both read the ignore set and then both replace the same author's
        // links (and an author refresh can never race a series refresh rewriting shared rows). See
        // the concurrency note on RefreshAuthorRosterCoreAsync.
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
                await RefreshAuthorRosterTrackedAsync(scraper, author);
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
                _logger.LogWarning(ex, "Failed to refresh the roster for author {PersonId}", author.Id);
            }
        }

        return (processed, succeeded, failed, stopReason);
    }

    /// <summary>
    /// Wraps <see cref="RefreshAuthorRosterCoreAsync"/> with the same failure bookkeeping
    /// <c>SeriesService.RefreshOneSeriesTrackedAsync</c> gives series refreshes: a fresh error
    /// replaces the author's stale one, a success clears it, and both the single and bulk refresh
    /// paths go through here so the tracked state never depends on which one was used. The daily
    /// request-budget exception is not a per-author failure - the bulk sweep stops on it rather
    /// than counting it - so it is re-thrown untouched rather than recorded.
    /// </summary>
    private async Task RefreshAuthorRosterTrackedAsync(IScraper scraper, Person person)
    {
        try
        {
            await RefreshAuthorRosterCoreAsync(scraper, person);
            await _authorConsistencyIssueRepository.DeleteByPersonIdAsync(person.Id);
        }
        catch (HardcoverDailyLimitExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _authorConsistencyIssueRepository.UpsertFailureAsync(person.Id, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// The one-author workload shared by the single, bulk and match-triggered refresh: fetch the
    /// author's full bibliography from the matched source - series books INCLUDED, since the
    /// unified <see cref="ExpectedBook"/> roster attributes every book to its author here while
    /// the series' own refresh stores its series placement, and one row serves both scopes - then
    /// upsert every book (resolving each book's source series to the matched local catalog row
    /// when one exists, by the exact (source name, source series id) pair, while unmatched source
    /// series keep their source-series fields for a later match to link), prune THIS author's
    /// links to exactly the fetched set, delete the orphans the prune leaves behind, and stamp
    /// <c>LastRefreshedAt</c>. The upsert carries no compilation data (author-shaped), so a
    /// series-linked row keeps the <c>IsCompilation</c> a series refresh established.
    ///
    /// The user's ignore decisions are preserved by the upsert itself: a refresh finds the
    /// existing unified row by its source identity (or adopts a legacy-copied row by natural key)
    /// and refreshes it in place, never resetting <see cref="ExpectedBook.IsIgnored"/> - there is
    /// no separate title-based carry-over anymore; the unified row is stable across refreshes.
    ///
    /// Because the refresh rewrites shared rows, it can change what a series' reconciliation
    /// renders (titles, release dates, series placements, or outright unlinking when the source
    /// stops reporting a book in a series). The cached reconciliation of EVERY local series the
    /// refresh touches - the author's rows before the rewrite and the rows the fetched books are
    /// (re)linked to - is invalidated, so the series detail never serves a stale view for the
    /// cache's whole TTL.
    ///
    /// This method is reachable concurrently from the controller's endpoints
    /// (<c>BrowseController.RefreshAuthor</c>, <c>RefreshAllAuthors</c> and the match-triggered
    /// refresh in <c>MatchAuthor</c>) and does not itself gate against that - the concurrency
    /// invariant is enforced by the caller. Every endpoint that reaches this REWRITE takes the
    /// SAME <see cref="IExpectedBookWriteGate"/> (mirroring <c>SeriesController</c>'s roster
    /// mutations) before calling in, so an author refresh can never race a series
    /// match/refresh/delete - or the bulk sweep - into a read-then-upsert-then-prune on the same
    /// unified rows, and a dismissal made between the upsert and the prune can never be lost.
    /// The single-row ignore-flag dismissals themselves take no gate (they are set-based single
    /// statements - see <see cref="IExpectedBookWriteGate"/>), which is safe here too: the
    /// dismissal resolves the row and writes its flag in one update, so the prune's set-based
    /// deletes serialize with it in SQLite instead of racing a tracked read-modify-write.
    ///
    /// An <see cref="AuthorNotFoundException"/> from the fetch propagates uncaught (as it does
    /// from <see cref="RefreshAllAuthorRostersAsync"/>'s failure tally and out of
    /// <c>RefreshAuthorRosterAsync</c> to the controller's 4xx mapping): the scrape failing to
    /// resolve the author is NOT an empty bibliography, and this method must abort before the
    /// upsert/prune so the stored roster - ignore history included - is left untouched.
    /// </summary>
    private async Task RefreshAuthorRosterCoreAsync(IScraper scraper, Person person)
    {
        // The local series names this refresh can change, collected up front and invalidated at
        // the end: the rows about to be rewritten (which may be unlinked or re-placed) plus the
        // matched lineages the fetched books land on. The read is the same bounded roster fetch
        // the reconciliation uses; an author past that pathological ceiling still gets as much
        // invalidation as the bounded reads saw, like every other best-effort cache.
        var touchedSeries = await CollectTouchedSeriesNamesAsync(person.Id);

        var books = await scraper.GetAuthorBooks(person.MatchedSourceId!);

        // Resolve each book's source series to a local matched catalog row once per distinct
        // (source name, source series id) pair - a bibliography with many entries of one series
        // must not repeat the lookup per book.
        var matchedSeriesBySourceKey = new Dictionary<(string SourceName, string SourceSeriesId), Series?>();

        var upserts = new List<ExpectedBookUpsert>(books.Count);
        foreach (var book in books)
        {
            long? seriesId = null;
            if (!string.IsNullOrEmpty(book.SeriesSourceId))
            {
                var sourceKey = (scraper.SourceName, book.SeriesSourceId!);
                if (!matchedSeriesBySourceKey.TryGetValue(sourceKey, out var matchedSeries))
                {
                    matchedSeries = await _seriesRepository.GetByMatchedSourceIdAsync(scraper.SourceName, book.SeriesSourceId!);
                    matchedSeriesBySourceKey[sourceKey] = matchedSeries;
                }

                seriesId = matchedSeries?.Id;
                if (matchedSeries is not null)
                {
                    touchedSeries.Add(matchedSeries.Name);
                }
            }

            upserts.Add(new ExpectedBookUpsert(
                SourceName: scraper.SourceName,
                SourceBookId: book.SourceBookId,
                Title: book.Title,
                Year: book.Year,
                ReleaseDate: book.ReleaseDate,
                SourceUrl: book.SourceUrl,
                ImageUrl: book.ImageUrl,
                SeriesId: seriesId,
                SourceSeriesId: book.SeriesSourceId,
                SourceSeriesName: book.SeriesName,
                SeriesPosition: book.SeriesPosition,
                // Author-shaped: the bibliography feed reports no compilation flag, so the poll
                // must not clear whatever a series refresh established on the same row.
                IsCompilation: null,
                Authors: new[] { new ExpectedBookAuthorLink(person.Id, person.Name) }));
        }

        var ids = await _expectedBookRepository.UpsertManyAsync(upserts);

        // Prune THIS author's links to exactly the fetched set. Other authors' links and any
        // other scope's series link keep their books alive; an empty bibliography prunes this
        // author's links to none and deletes only the books that are left with no author and no
        // series at all.
        await _expectedBookRepository.PruneAuthorLinksAsync(person.Id, ids);
        await _expectedBookRepository.DeleteOrphanExpectedBooksAsync();

        await _personRepository.SetLastRefreshedAtAsync(person.Id, DateTime.UtcNow);

        foreach (var seriesName in touchedSeries)
        {
            _seriesReconciliationCache.Invalidate(seriesName);
        }
    }

    /// <summary>
    /// The names of every local series the author's current roster rows are linked to - captured
    /// BEFORE a refresh rewrites them, so a series placement the rewrite is about to change, and
    /// a series it is about to unlink from, are both invalidated. Read through the same bounded
    /// <see cref="IExpectedBookRepository.GetByAuthorBoundedAsync"/> the reconciliation uses.
    /// </summary>
    private async Task<HashSet<string>> CollectTouchedSeriesNamesAsync(long personId)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var (expected, _) = await _expectedBookRepository.GetByAuthorBoundedAsync(
            personId, AuthorReconciliationProvider.MaxReconciliationRosterEntries);
        foreach (var book in expected)
        {
            if (book.Series?.Name is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }

        return names;
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