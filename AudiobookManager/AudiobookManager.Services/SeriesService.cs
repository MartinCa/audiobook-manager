using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Database.Search;
using AudiobookManager.Domain;
using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Services.MappingExtensions;
using Microsoft.Extensions.Logging;
using PendingSeriesRefresh = AudiobookManager.Domain.PendingSeriesRefresh;

namespace AudiobookManager.Services;

public class SeriesService : ISeriesService
{
    /// <summary>
    /// Minimum normalized title similarity for a library book to surface as a candidate for a
    /// missing expected book. Deliberately looser than the matcher's
    /// <see cref="SeriesRosterMatcher.TitleMatchThreshold"/>: these are advisory candidates a
    /// human confirms, and a subtitled or lightly renamed edition must still surface - a false
    /// positive costs a glance, a false negative hides the very book the user is looking for.
    /// </summary>
    private const double CandidateTitleSimilarityThreshold = 0.7;

    /// <summary>
    /// Author similarity that counts as an author match for candidate ranking - the same bar
    /// <see cref="ScoreCandidate"/> applies inline when corroborating a series candidate with an
    /// author overlap.
    /// </summary>
    private const double AuthorMatchSimilarityThreshold = 0.85;

    /// <summary>
    /// Cap on the missing-book candidate list - the endpoint's bound, per the repo's "no
    /// unbounded lists over the wire" rule. Candidates are ranked, so anything past this many is
    /// noise even for a generic title.
    /// </summary>
    public const int MaxMissingBookCandidates = 20;

    /// <summary>
    /// SQL LIKE pre-filter row cap for <see cref="IAudiobookRepository.GetSeriesCandidateDataAsync"/>.
    /// The service ranking is authoritative; the pre-filter only reduces the set the fuzzy
    /// scorer works against. Increased to 1000 to improve recall for multi-token titles while
    /// final results remain capped at <see cref="MaxMissingBookCandidates"/>.
    /// </summary>
    private const int CandidatePrefilterLimit = 1000;

    /// <summary>
    /// Cap on the advisory series-part conflict check's result. The check runs on every edit
    /// keystroke, is bounded by design, and the shared (series, part) combination is a small set
    /// in any sane library - a pathological one is truncated rather than materialized whole.
    /// </summary>
    public const int MaxSeriesPartConflictRows = 50;

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly IPersonRepository _personRepository;
    private readonly IExpectedBookRepository _expectedBookRepository;
    private readonly ISeriesFollowRepository _seriesFollowRepository;
    private readonly ISeriesMappingRepository _seriesMappingRepository;
    private readonly IPendingSeriesRefreshRepository _pendingSeriesRefreshRepository;
    private readonly IAudiobookService _audiobookService;
    private readonly IAudiobookSaveGate _saveGate;
    private readonly ILibraryConsistencyService _libraryConsistencyService;
    private readonly ISeriesReconciliationCache _reconciliationCache;
    private readonly ISeriesReconciliationProvider _reconciliation;
    private readonly ISimilarValueDetectionCache _similarValueDetectionCache;
    private readonly IEnumerable<IScraper> _scrapers;
    private readonly ILogger<SeriesService> _logger;

    public SeriesService(
        IAudiobookRepository audiobookRepository,
        ISeriesRepository seriesRepository,
        IPersonRepository personRepository,
        IExpectedBookRepository expectedBookRepository,
        ISeriesFollowRepository seriesFollowRepository,
        ISeriesMappingRepository seriesMappingRepository,
        IPendingSeriesRefreshRepository pendingSeriesRefreshRepository,
        IAudiobookService audiobookService,
        IAudiobookSaveGate saveGate,
        ILibraryConsistencyService libraryConsistencyService,
        ISeriesReconciliationCache reconciliationCache,
        ISeriesReconciliationProvider reconciliation,
        ISimilarValueDetectionCache similarValueDetectionCache,
        IEnumerable<IScraper> scrapers,
        ILogger<SeriesService> logger)
    {
        _audiobookRepository = audiobookRepository;
        _seriesRepository = seriesRepository;
        _personRepository = personRepository;
        _expectedBookRepository = expectedBookRepository;
        _seriesFollowRepository = seriesFollowRepository;
        _seriesMappingRepository = seriesMappingRepository;
        _pendingSeriesRefreshRepository = pendingSeriesRefreshRepository;
        _audiobookService = audiobookService;
        _saveGate = saveGate;
        _libraryConsistencyService = libraryConsistencyService;
        _reconciliationCache = reconciliationCache;
        _reconciliation = reconciliation;
        _similarValueDetectionCache = similarValueDetectionCache;
        _scrapers = scrapers;
        _logger = logger;
    }

    private IEnumerable<IScraper> SeriesCapableScrapers =>
        _scrapers.Where(s => s.SupportsSeriesLookup && (!s.RequiresApiKey || s.IsApiKeyConfigured));

    public async Task<List<SeriesOverview>> GetAllSeriesOverviewAsync()
    {
        // Only the series value, the owned-matching fields and the author names are needed
        // here - loading every audiobook's narrators and genres would be pure waste.
        var books = await _audiobookRepository.GetSeriesGroupingDataAsync();
        var catalog = await _seriesRepository.GetAllWithExpectedBooksAsync();
        var catalogByName = catalog.ToDictionary(s => s.Name, StringComparer.Ordinal);

        var overviews = books
            .Where(b => !string.IsNullOrWhiteSpace(b.Series))
            .GroupBy(b => b.Series, StringComparer.Ordinal)
            .Select(group =>
            {
                catalogByName.TryGetValue(group.Key, out var catalogRow);
                return BuildOverview(group.Key, group.ToList(), catalogRow);
            })
            .ToList();

        // Catalog rows whose series value no longer appears on any audiobook are still worth
        // listing - the user may have renamed or removed the last owned book of the series.
        var ownedNames = new HashSet<string>(overviews.Select(o => o.Name), StringComparer.Ordinal);
        foreach (var orphanRow in catalog.Where(s => !ownedNames.Contains(s.Name)))
        {
            overviews.Add(BuildOverview(orphanRow.Name, new List<SeriesGroupingBook>(), orphanRow));
        }

        return overviews.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<SeriesOverviewPage> GetSeriesOverviewPageAsync(
        int page, int pageSize, string? search, bool? matched, long? authorId = null,
        SeriesOverviewFilter? filter = null)
    {
        // HasMissingBooks/HasUpcomingBooks depend on the fuzzy roster reconciliation
        // (SeriesRosterMatcher), which the repository layer does not reference - resolve them
        // here into a restricting name set before the paged SQL query runs. This is a
        // whole-library computation, the same one BulkAutoMatchSeriesAsync already pays for; it
        // only runs when the caller actually asks for one of these two filters.
        IReadOnlyCollection<string>? restrictToNames = null;
        if (filter?.NeedsReconciliation == true)
        {
            var allOverviews = await GetAllSeriesOverviewAsync();
            restrictToNames = allOverviews
                .Where(o => (filter.HasMissingBooks is null || (o.MissingBookCount > 0) == filter.HasMissingBooks)
                    && (filter.HasUpcomingBooks is null || (o.UpcomingBookCount > 0) == filter.HasUpcomingBooks))
                .Select(o => o.Name)
                .ToList();
        }

        var (names, totalCount) = await _audiobookRepository.GetSeriesValuesPageAsync(
            search, matched, skip: (int)((long)page * pageSize), take: pageSize, authorId, filter, restrictToNames);

        if (names.Count == 0)
        {
            return new SeriesOverviewPage { Items = new List<SeriesOverview>(), TotalCount = totalCount };
        }

        var booksBySeries = (await _audiobookRepository.GetSeriesGroupingDataAsync(names))
            .ToLookup(b => b.Series, StringComparer.Ordinal);
        var catalogByName = (await _seriesRepository.GetByNamesWithExpectedBooksAsync(names))
            .ToDictionary(s => s.Name, StringComparer.Ordinal);
        // Bulk follow-status lookup, not one query per row - see GetFollowedSeriesNamesAsync.
        var followedNames = await _seriesFollowRepository.GetFollowedSeriesNamesAsync(names);

        var items = names.Select(name =>
        {
            catalogByName.TryGetValue(name, out var catalogRow);
            var overview = BuildOverview(name, booksBySeries[name].ToList(), catalogRow);
            overview.IsFollowed = followedNames.Contains(name);
            return overview;
        }).ToList();

        return new SeriesOverviewPage { Items = items, TotalCount = totalCount };
    }

    public async Task<SeriesOverviewCounts> GetSeriesOverviewCountsAsync()
    {
        var (total, matched) = await _audiobookRepository.GetSeriesValueCountsAsync();
        return new SeriesOverviewCounts { Total = total, Matched = matched, Unmatched = total - matched };
    }

    public async Task<SeriesDetailPage?> GetSeriesDetailPageAsync(
        string seriesName,
        int ownedSkip, int ownedTake,
        int missingSkip, int missingTake,
        int ignoredMissingSkip, int ignoredMissingTake,
        int ignoredUpcomingSkip, int ignoredUpcomingTake,
        int partMismatchSkip, int partMismatchTake,
        int upcomingSkip = 0, int upcomingTake = int.MaxValue)
    {
        // The per-request reads are bounded: one catalog metadata row, one SQL page of owned
        // books, and the cached reconciliation. The reconciliation itself - which classifies the
        // whole roster against the series' owned keys via the fuzzy matcher - is computed once
        // per series per change, never per page request (see GetReconciliationAsync).
        var catalogRow = await _seriesRepository.GetByNameAsync(seriesName);
        var ownedPage = await _audiobookRepository.GetSeriesOwnedBooksPageAsync(seriesName, ownedSkip, ownedTake);

        if (ownedPage.Total == 0 && catalogRow is null)
        {
            return null;
        }

        var reconciliation = await GetReconciliationAsync(seriesName);

        return new SeriesDetailPage
        {
            Overview = BuildReconciledOverview(seriesName, catalogRow, reconciliation),
            OwnedBooks = ownedPage.Items
                .Select(b => new SeriesOwnedBook
                {
                    Id = b.Id,
                    BookName = b.BookName,
                    SeriesPart = b.SeriesPart,
                    Year = b.Year,
                    Authors = b.Authors,
                    Narrators = b.Narrators,
                    DurationInSeconds = b.DurationInSeconds,
                    CoverFilePath = b.CoverFilePath,
                })
                .ToList(),
            OwnedBookTotal = ownedPage.Total,
            MissingBooks = reconciliation.Missing.Skip(missingSkip).Take(missingTake).ToList(),
            MissingBookTotal = reconciliation.Missing.Count,
            UpcomingBooks = reconciliation.Upcoming.Skip(upcomingSkip).Take(upcomingTake).ToList(),
            UpcomingBookTotal = reconciliation.Upcoming.Count,
            // The reconciliation already split the ignored rows by the same classifier (and the
            // same "today") it used for Missing/Upcoming, so each section's ignored sub-list pages
            // with its own cursor and reports its own true total - never the combined count.
            IgnoredMissingBooks = reconciliation.IgnoredMissing.Skip(ignoredMissingSkip).Take(ignoredMissingTake).ToList(),
            IgnoredMissingBookTotal = reconciliation.IgnoredMissing.Count,
            IgnoredUpcomingBooks = reconciliation.IgnoredUpcoming.Skip(ignoredUpcomingSkip).Take(ignoredUpcomingTake).ToList(),
            IgnoredUpcomingBookTotal = reconciliation.IgnoredUpcoming.Count,
            PartMismatches = reconciliation.PartMismatches.Skip(partMismatchSkip).Take(partMismatchTake).ToList(),
            PartMismatchTotal = reconciliation.PartMismatchCount,
        };
    }

    /// <summary>
    /// Delegates to <see cref="ISeriesReconciliationProvider"/>, which owns the computation and
    /// its cache. It stays on <see cref="ISeriesService"/> because the series-facing callers read
    /// it through this service; the split exists to keep the consistency graph off the whole of
    /// this type (see that interface for the cycle it broke), not to move the concept.
    /// </summary>
    public Task<SeriesReconciliation> GetReconciliationAsync(string seriesName) =>
        _reconciliation.GetReconciliationAsync(seriesName);

    public async Task<List<SeriesMatchCandidate>> SuggestSeriesMatchesAsync(string seriesName)
    {
        var knownAuthors = await GetKnownAuthorsAsync(seriesName);

        return await SuggestSeriesMatchesAsync(seriesName, knownAuthors);
    }

    public async Task<List<SeriesMatchCandidate>> SearchSeriesMatchesAsync(string seriesName, string query)
    {
        var trimmedQuery = query?.Trim();
        if (string.IsNullOrEmpty(trimmedQuery))
        {
            return new List<SeriesMatchCandidate>();
        }

        var knownAuthors = await GetKnownAuthorsAsync(seriesName);

        return Uri.TryCreate(trimmedQuery, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? await LookupSeriesByUrlAsync(seriesName, knownAuthors, trimmedQuery)
                : await SuggestSeriesMatchesAsync(seriesName, knownAuthors, trimmedQuery);
    }

    private async Task<List<string>> GetKnownAuthorsAsync(string seriesName) =>
        await _audiobookRepository.GetAuthorNamesBySeriesAsync(seriesName);

    /// <summary>
    /// Resolves a single series directly from a source URL the user pasted in, rather than
    /// searching - only the source whose SupportsUrl matches is asked, and the result (if any)
    /// comes back as a one-item candidate list so the UI flow is identical to a search result.
    /// </summary>
    private async Task<List<SeriesMatchCandidate>> LookupSeriesByUrlAsync(
        string seriesName, IReadOnlyCollection<string> knownAuthors, string url)
    {
        var scraper = SeriesCapableScrapers.FirstOrDefault(s => s.SupportsUrl(url));
        if (scraper is null)
        {
            return new List<SeriesMatchCandidate>();
        }

        try
        {
            var result = await scraper.GetSeriesBooks(url);
            if (result is null)
            {
                return new List<SeriesMatchCandidate>();
            }

            return new List<SeriesMatchCandidate>
            {
                new()
                {
                    SourceName = scraper.SourceName,
                    SourceId = result.SourceId,
                    SeriesName = result.SeriesName,
                    SourceUrl = result.SourceUrl,
                    Authors = result.Authors.ToList(),
                    BookCount = result.BookCount,
                    Confidence = ScoreCandidate(seriesName, knownAuthors, result.SeriesName, result.Authors),
                },
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Manual series URL lookup failed for source {Source} and url {Url}", scraper.SourceName, url);
            return new List<SeriesMatchCandidate>();
        }
    }

    /// <summary>
    /// Candidate lookup for callers that already know the series' authors (the bulk
    /// auto-match has them from the overview it just loaded), so no extra query is needed.
    /// <paramref name="searchTerm"/> defaults to <paramref name="seriesName"/> but can be
    /// overridden for a manual search that doesn't match the library's own series value.
    /// </summary>
    private async Task<List<SeriesMatchCandidate>> SuggestSeriesMatchesAsync(
        string seriesName,
        IReadOnlyCollection<string> knownAuthors,
        string? searchTerm = null)
    {
        var candidates = new List<SeriesMatchCandidate>();

        foreach (var scraper in SeriesCapableScrapers)
        {
            try
            {
                var results = await scraper.SearchSeries(searchTerm ?? seriesName);
                foreach (var result in results)
                {
                    candidates.Add(new SeriesMatchCandidate
                    {
                        SourceName = scraper.SourceName,
                        SourceId = result.SourceId,
                        SeriesName = result.SeriesName,
                        SourceUrl = result.SourceUrl,
                        Authors = result.Authors.ToList(),
                        BookCount = result.BookCount,
                        Confidence = ScoreCandidate(seriesName, knownAuthors, result.SeriesName, result.Authors),
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Series search failed for source {Source} and series {SeriesName}", scraper.SourceName, seriesName);
            }
        }

        return candidates
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => c.SeriesName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SeriesOverview> MatchSeriesAsync(string seriesName, string sourceName, string sourceSeriesId, double? confidence = null, bool includeOmnibusEditions = false)
    {
        var saved = await MatchSeriesCoreAsync(seriesName, sourceName, sourceSeriesId, confidence, includeOmnibusEditions);

        // MatchSeriesCoreAsync invalidated the cache; rebuild the overview from a fresh
        // reconciliation so the return value already reflects the newly-stored roster. Bulk
        // callers use MatchSeriesCoreAsync directly and never render an overview.
        var reconciliation = await GetReconciliationAsync(seriesName);
        return BuildReconciledOverview(seriesName, saved, reconciliation);
    }

    public async Task<SeriesOverview> SetIncludeOmnibusEditionsAsync(string seriesName, bool includeOmnibusEditions)
    {
        // The full roster (compilations included) is always stored, so this is a pure display
        // setting - no re-fetch from the source is needed to apply it. It does change which
        // roster entries are visible, so the cached reconciliation must be dropped and rebuilt.
        var saved = await _seriesRepository.SetIncludeOmnibusEditionsAsync(seriesName, includeOmnibusEditions);
        _reconciliationCache.Invalidate(seriesName);

        var reconciliation = await GetReconciliationAsync(seriesName);
        return BuildReconciledOverview(seriesName, saved, reconciliation);
    }

    public async Task<List<Domain.SeriesMapping>> GetSeriesMappingsAsync(string seriesName)
    {
        // The list comes back capped at the repository's per-series limit (bounded-list
        // invariant): the bound lives in the query, so it holds no matter how many patterns
        // the series owns.
        var mappings = await _seriesMappingRepository.GetBySeriesNameAsync(seriesName);
        return mappings.Select(SeriesMappingMapping.ToDomain).ToList();
    }

    public async Task<Domain.SeriesMapping> CreateSeriesMappingAsync(string seriesName, Domain.SeriesMapping seriesMapping)
    {
        EnsurePatternCompiles(seriesMapping.Regex);

        // An unmatched series exists only as a value on audiobooks and has no catalog row, but a
        // mapping pattern is data-model-wise owned by a Series row - so creating one also creates
        // the owning row. SeriesRepository.GetOrCreateByNameAsync tolerates the read-then-insert
        // race the same way the other upserts do, and reports whether THIS call inserted the row,
        // which is what lets a failed insert roll the owner back (below).
        var (series, createdOwner) = await _seriesRepository.GetOrCreateByNameAsync(seriesName);

        try
        {
            var dbModel = seriesMapping.ToDb(series.Id);
            dbModel = await _seriesMappingRepository.CreateSeriesMappingAsync(dbModel);
            return dbModel.ToDomain();
        }
        catch (Exception)
        {
            if (createdOwner)
            {
                // A duplicate-regex failure (or any other insert failure) must not leave the owner
                // row this call just inserted behind as an orphan: an unmatched series exists only
                // as a value on audiobooks, so an empty catalog row with no pattern would surface
                // it as a phantom series on the overview. The rollback is a single conditional
                // delete - only a row still holding nothing but what this call created (unmatched,
                // no patterns, no roster, no omnibus flag) is removed, re-checked atomically at
                // delete time. A concurrent caller that lost the create race but inserted its own
                // pattern (or matched the series, or toggled omnibus) onto this row in the window
                // since it was created keeps its data: deleting the row would cascade it all away
                // with no error surfaced to that caller, which believes its write succeeded.
                // createdOwner is still worth checking first - a pre-existing owner (or a winner
                // adopted after a concurrent create) is never even considered for rollback.
                try
                {
                    await _seriesRepository.DeleteIfEmptyAsync(series.Id);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx,
                        "Failed to roll back the series owner row {SeriesId} after a mapping insert failure",
                        series.Id);
                }
            }
            throw;
        }
    }

    public async Task<Domain.SeriesMapping?> UpdateSeriesMappingAsync(string seriesName, long mappingId, Domain.SeriesMapping seriesMapping)
    {
        EnsurePatternCompiles(seriesMapping.Regex);

        var series = await _seriesRepository.GetByNameAsync(seriesName);
        if (series is null)
        {
            return null;
        }

        var existing = await _seriesMappingRepository.GetSeriesMappingAsync(mappingId);
        if (existing is null || existing.SeriesId != series.Id)
        {
            // The mapping (or the ownership the caller claims) does not exist in this series'
            // scope - an id that belongs to another series is not this series' mapping.
            return null;
        }

        seriesMapping.Id = mappingId;
        var updated = await _seriesMappingRepository.UpdateSeriesMappingAsync(seriesMapping.ToDb(series.Id));
        return updated?.ToDomain();
    }

    /// <summary>
    /// Refuses a mapping pattern that is not a valid regular expression, at the point the user
    /// submits it. Without this the row is accepted and then silently skipped every time the
    /// mappings load - the pattern simply never fires, with only a server log line saying why,
    /// which is indistinguishable from a pattern that is valid but matches nothing.
    ///
    /// ArgumentException is what the controllers translate into a 400 carrying the message, and
    /// the framework's own regex-parse message names the offending position and construct.
    /// </summary>
    private static void EnsurePatternCompiles(string pattern)
    {
        if (!SeriesMappingPattern.TryCompile(pattern, out _, out var error))
        {
            throw new ArgumentException($"'{pattern}' is not a valid regular expression: {error}");
        }
    }

    public async Task<bool> DeleteSeriesMappingAsync(string seriesName, long mappingId)
    {
        var series = await _seriesRepository.GetByNameAsync(seriesName);
        if (series is null)
        {
            return false;
        }

        var existing = await _seriesMappingRepository.GetSeriesMappingAsync(mappingId);
        if (existing is null || existing.SeriesId != series.Id)
        {
            return false;
        }

        return await _seriesMappingRepository.DeleteSeriesMappingAsync(mappingId);
    }

    /// <summary>
    /// Matches the series to a source and stores its roster on the unified expected-books table
    /// (<see cref="IExpectedBookRepository.UpsertManyAsync"/>), returning the catalog row. A
    /// caller that already fetched the roster (the refresh path needs it to compute its diff)
    /// passes it in via <paramref name="fetched"/> so the source is not hit twice. Books the
    /// source no longer reports are unlinked from the series (their rows survive, author-linked
    /// or other-series-linked) and the resulting orphans are deleted.
    /// </summary>
    private async Task<Series> MatchSeriesCoreAsync(
        string seriesName,
        string sourceName,
        string sourceSeriesId,
        double? confidence,
        bool includeOmnibusEditions,
        SeriesSearchResult? fetched = null)
    {
        var scraper = SeriesCapableScrapers.FirstOrDefault(s => s.IsSource(sourceName))
            ?? throw new ArgumentException($"No series-capable scraper for source {sourceName}");

        var roster = fetched
            ?? await scraper.GetSeriesBooks(sourceSeriesId)
            ?? throw new Exception($"Source {sourceName} returned no series for id {sourceSeriesId}");

        var saved = await _seriesRepository.UpsertSeriesAsync(new Series
        {
            Name = seriesName,
            MatchedSourceName = scraper.SourceName,
            MatchedSourceId = roster.SourceId,
            MatchedSourceUrl = roster.SourceUrl,
            MatchedSeriesName = roster.SeriesName,
            MatchConfidence = confidence,
            LastRefreshedAt = DateTime.UtcNow,
            IncludeOmnibusEditions = includeOmnibusEditions,
        });

        // The roster entries' author names are source spellings. They are resolved against the
        // library's existing Person rows so an author-linked book can be found by both scopes -
        // but never created from scrape data: an unknown name stays a name-only link. Resolved in
        // one batched call - this runs under the process-wide write gate, and the old per-name
        // loop was one DB round trip per distinct contributor of the roster.
        var rosterAuthorNames = roster.Books
            .SelectMany(b => b.Authors)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var personsByName = await _personRepository.GetByNamesAsync(rosterAuthorNames);

        // The full roster is always stored, compilations included - IncludeOmnibusEditions only
        // controls what SeriesService treats as visible when reading it back, so toggling it
        // later doesn't require re-fetching from the source. Ignore decisions are NOT carried
        // here: UpsertAsync refreshes an existing row in place and never resets IsIgnored, so a
        // user's dismissals survive the re-match/refresh automatically.
        var upserts = roster.Books.Select(b => new ExpectedBookUpsert(
            SourceName: scraper.SourceName,
            SourceBookId: b.SourceBookId,
            Title: b.Title,
            Year: b.Year,
            ReleaseDate: b.ReleaseDate,
            SourceUrl: b.SourceUrl,
            ImageUrl: b.ImageUrl,
            SeriesId: saved.Id,
            SourceSeriesId: sourceSeriesId,
            SourceSeriesName: roster.SeriesName,
            SeriesPosition: b.Position,
            // Series-shaped: the source's compilation flag is authoritative for the row.
            IsCompilation: b.IsCompilation,
            Authors: b.Authors.Select(name => new ExpectedBookAuthorLink(personsByName.GetValueOrDefault(name)?.Id, name)).ToList()))
            .ToList();

        var ids = await _expectedBookRepository.UpsertManyAsync(upserts);

        // Detach rows the source no longer reports from this series, then delete the ones that
        // no other scope links to - an author-linked book discovered by an author refresh stays.
        await _expectedBookRepository.UnlinkSeriesBooksAsync(saved.Id, ids);
        await _expectedBookRepository.DeleteOrphanExpectedBooksAsync();

        // The roster was just re-stored - the reconciled detail is stale by definition.
        _reconciliationCache.Invalidate(seriesName);

        return saved;
    }

    public async Task<(int Processed, int Succeeded, int Failed, string? StopReason)> BulkAutoMatchSeriesAsync(
        double confidenceThreshold,
        List<string>? seriesNames,
        Func<int, int, int, int, Task> progressAction)
    {
        var overviews = await GetAllSeriesOverviewAsync();

        var unmatched = overviews
            .Where(o => !o.IsMatched)
            .Where(o => seriesNames is null || seriesNames.Contains(o.Name, StringComparer.Ordinal))
            .ToList();

        // The overview already carries each series' authors and any omnibus-inclusion setting
        // recorded before it was matched, so the candidate lookup below must not re-query them.
        var authorsByName = unmatched.ToDictionary(o => o.Name, o => o.Authors, StringComparer.Ordinal);
        var includeOmnibusByName = unmatched.ToDictionary(o => o.Name, o => o.IncludeOmnibusEditions, StringComparer.Ordinal);
        var targets = unmatched.Select(o => o.Name).ToList();

        return await RunBulkAsync(targets, progressAction, async name =>
        {
            var knownAuthors = authorsByName.TryGetValue(name, out var authors)
                ? authors
                : new List<string>();
            var candidates = await SuggestSeriesMatchesAsync(name, knownAuthors);
            var top = candidates.FirstOrDefault();

            if (top is null || top.Confidence < confidenceThreshold)
            {
                _logger.LogInformation(
                    "Skipping auto-match for series {SeriesName}: best confidence {Confidence} below threshold {Threshold}",
                    name, top?.Confidence ?? 0, confidenceThreshold);
                return false;
            }

            var includeOmnibusEditions = includeOmnibusByName.GetValueOrDefault(name);
            await MatchSeriesCoreAsync(name, top.SourceName, top.SourceId, top.Confidence, includeOmnibusEditions);
            return true;
        });
    }

    /// <summary>
    /// Refreshes one series from its matched source, synchronously: the fetch is bounded by the
    /// scraper's own HTTP timeouts, the same latency profile the single-book metadata refresh
    /// already has. Refreshing always re-fetches and re-stores the roster and stamps
    /// <c>LastRefreshedAt</c>; a refresh that found explicit changes (part updates, missing
    /// source books, part removals) also stores a pending snapshot for the series, and a
    /// no-change refresh clears any stale one so it never lingers in the pending list. Throws
    /// <see cref="KeyNotFoundException"/> when the series is not in the catalog or has no
    /// matched source.
    /// </summary>
    public async Task<SeriesRefreshResult> RefreshSeriesAsync(string seriesName)
    {
        // The roster itself is not read here - MatchSeriesCoreAsync re-fetches and re-stores it
        // from the source - but the catalog row is still loaded via GetByNameWithExpectedBooksAsync
        // so it reports the same shape as the bulk sweep below. The user's ignore decisions need
        // no carrying: UpsertAsync refreshes each row in place and never resets IsIgnored.
        var row = await _seriesRepository.GetByNameWithExpectedBooksAsync(seriesName);
        if (row is null || string.IsNullOrEmpty(row.MatchedSourceName) || string.IsNullOrEmpty(row.MatchedSourceId))
        {
            throw new KeyNotFoundException($"Series '{seriesName}' is not matched to a metadata source, so it cannot be refreshed.");
        }

        var (hasChanges, changeCount, sourceName) = await RefreshOneSeriesCoreAsync(seriesName, row);
        return new SeriesRefreshResult(Success: true, hasChanges, changeCount, sourceName);
    }

    public async Task<(int Processed, int Succeeded, int Failed, string? StopReason)> RefreshAllSeriesAsync(
        Func<int, int, int, int, Task> progressAction)
    {
        var catalog = await _seriesRepository.GetAllWithExpectedBooksAsync();
        var matchedNames = catalog
            .Where(s => !string.IsNullOrEmpty(s.MatchedSourceName) && !string.IsNullOrEmpty(s.MatchedSourceId))
            .Select(s => s.Name)
            .ToList();

        return await RefreshManyAsync(matchedNames, progressAction);
    }

    public async Task IgnoreExpectedBookAsync(string seriesName, string? position, string? title, bool ignored)
    {
        await _seriesRepository.SetExpectedBookIgnoredAsync(
            seriesName, position, title, ignored, SeriesReconciliationProvider.MaxReconciliationRosterEntries);
        _reconciliationCache.Invalidate(seriesName);
    }

    public async Task<List<SeriesBookCandidate>> FindMissingBookCandidatesAsync(string seriesName, string? position, string? title)
    {
        var expected = await _seriesRepository.FindExpectedBookAsync(seriesName, position, title)
            ?? throw new KeyNotFoundException(
                $"Expected book (position '{position}', title '{title}') not found in series '{seriesName}'");

        // Deliberately sequential: GetAuthorNamesBySeriesAsync and GetSeriesCandidateDataAsync
        // both go through the repo layer's single scoped DatabaseContext (AddDbContext in
        // Database/DependencyInjection.cs), so firing "Task.WhenAll" at them would start a
        // second query on the same context while the first is still in flight and throw EF's
        // "A second operation was started on this context instance" InvalidOperationException.
        // The concurrency would be real - the two reads share nothing - but the context's
        // single-connection rule forbids it; the codebase keeps every such pair sequential for
        // the same reason.
        var knownAuthors = await GetKnownAuthorsAsync(seriesName);
        return await FindCandidatesForTitleAsync(expected.Title, knownAuthors);
    }

    public async Task<SeriesBulkCandidatePage> GetBulkMissingBookCandidatesAsync(string seriesName, int skip, int take)
    {
        // The missing books are the cached reconciliation's list (bounded at compute time and
        // computed once per series per change), so paging never re-reads the roster or the
        // owned set per page request. The per-row candidate ranking needs the series' authors
        // and the bounded candidate pre-filter, both read once per page.
        var reconciliation = await GetReconciliationAsync(seriesName);
        var page = reconciliation.Missing.Skip(skip).Take(take).ToList();
        if (page.Count == 0)
        {
            return new SeriesBulkCandidatePage { Items = new List<SeriesBulkCandidateItem>(), TotalCount = reconciliation.MissingBookCount };
        }

        var knownAuthors = await GetKnownAuthorsAsync(seriesName);

        var items = new List<SeriesBulkCandidateItem>();
        foreach (var expected in page)
        {
            items.Add(new SeriesBulkCandidateItem
            {
                Book = expected,
                Candidates = await FindCandidatesForTitleAsync(expected.Title, knownAuthors),
            });
        }

        return new SeriesBulkCandidatePage { Items = items, TotalCount = reconciliation.MissingBookCount };
    }

    /// <summary>
    /// The ranked candidate list for one missing expected book's title, shared by the single-book
    /// candidates endpoint and the bulk view so both accept the same matches. The ranking is
    /// authoritative; the SQL LIKE pre-filter on <see cref="CandidatePrefilterLimit"/> rows only
    /// reduces the set the fuzzy scorer works against, and the final list is capped at
    /// <see cref="MaxMissingBookCandidates"/>.
    /// </summary>
    private async Task<List<SeriesBookCandidate>> FindCandidatesForTitleAsync(
        string title, IReadOnlyCollection<string> knownAuthors)
    {
        var books = await _audiobookRepository.GetSeriesCandidateDataAsync(title, CandidatePrefilterLimit);

        // A book already in the target series is deliberately NOT excluded: one with a wrong part
        // or a slightly different title is exactly what leaves a roster entry reported as missing,
        // and the candidate carries its current series/part so the UI can show it.
        return books
            .Select(book => (Book: book, TitleSimilarity: TitleSimilarity(book.BookName, title)))
            .Where(b => b.TitleSimilarity >= CandidateTitleSimilarityThreshold)
            .Select(b =>
            {
                // The roster carries no author of its own, so the series' known authors (the
                // authors of its owned books) are the author evidence a candidate is compared
                // against. Tier 1 needs a close match on one of them; without it the candidate is
                // tier 2, a title-only match.
                var authorSimilarity = knownAuthors
                    .SelectMany(known => b.Book.Authors, (known, candidateAuthor) => TitleSimilarity(known, candidateAuthor))
                    .DefaultIfEmpty(0)
                    .Max();

                return (b.Book, b.TitleSimilarity, AuthorSimilarity: authorSimilarity);
            })
            .OrderByDescending(b => b.AuthorSimilarity >= AuthorMatchSimilarityThreshold)
            .ThenByDescending(b => b.TitleSimilarity)
            .ThenByDescending(b => b.AuthorSimilarity)
            .ThenBy(b => b.Book.BookName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Book.Id)
            .Take(MaxMissingBookCandidates)
            .Select(b => new SeriesBookCandidate
            {
                AudiobookId = b.Book.Id,
                BookName = b.Book.BookName,
                Series = b.Book.Series,
                SeriesPart = b.Book.SeriesPart,
                Year = b.Book.Year,
                Authors = b.Book.Authors,
                TitleSimilarity = Math.Round(b.TitleSimilarity, 4),
                AuthorMatches = b.AuthorSimilarity >= AuthorMatchSimilarityThreshold,
            })
            .ToList();
    }

    public async Task<SeriesExpectedBookInfo?> ResolveExpectedBookAsync(string seriesName, string? position, string? title)
    {
        // The same repository lookup ApplyMissingBookAsync performs against the same key, so the
        // bulk apply's pre-flight duplicate check and the apply itself can never disagree about
        // which natural key addresses which roster entry.
        var book = await _seriesRepository.FindExpectedBookStrictAsync(seriesName, position, title);
        return book is null ? null : SeriesReconciliationProvider.ToExpectedInfo(book);
    }

    public async Task ApplyMissingBookAsync(string seriesName, string? position, string? title, long audiobookId)
    {
        var expected = await _seriesRepository.FindExpectedBookStrictAsync(seriesName, position, title)
            ?? throw new KeyNotFoundException(
                $"Expected book (position '{position}', title '{title}') not found in series '{seriesName}'");

        var audiobook = await _audiobookService.GetAudiobookById(audiobookId)
            ?? throw new KeyNotFoundException($"Audiobook {audiobookId} not found");

        // Only the series assignment changes - the book keeps its own name, authors and year. The
        // write goes through UpdateAudiobook so the m4b tags, the recomputed library path (and any
        // relocation it implies), the sidecars and the database all update together, per the
        // binding invariant. A roster entry with no position clears the part.
        audiobook.Series = seriesName;
        audiobook.SeriesPart = expected.SeriesPosition;

        await _audiobookService.UpdateAudiobook(audiobookId, audiobook);
    }

    /// <summary>
    /// Other books already carrying the given (series, series part) combination, excluding the
    /// current book. Advisory: a blank series or part returns nothing (an empty part cannot be
    /// equivalent to any part via <see cref="AudiobookManager.Database.Search.SeriesPartEquivalence.PartsEquivalentClr"/>, and is
    /// its own informational state in the edit form anyway). The equivalence is applied in SQL,
    /// so the returned conflicts are exact and nothing is silently skipped; the cap and its
    /// truncation flag are carried back for the UI to surface.
    /// </summary>
    public async Task<SeriesPartConflictCheck> GetSeriesPartConflictsAsync(
        long currentAudiobookId, string? series, string? seriesPart, int limit = MaxSeriesPartConflictRows)
    {
        var trimmedSeries = series?.Trim();
        var trimmedPart = seriesPart?.Trim();
        if (string.IsNullOrEmpty(trimmedSeries) || string.IsNullOrEmpty(trimmedPart))
        {
            return new SeriesPartConflictCheck(new List<SeriesPartConflict>(), Truncated: false);
        }

        var (rows, truncated) = await _audiobookRepository.GetSeriesPartConflictCandidatesAsync(
            trimmedSeries, currentAudiobookId, trimmedPart, limit);

        return new SeriesPartConflictCheck(
            rows.Select(c => new SeriesPartConflict(c.AudiobookId, c.BookName, c.SeriesPart)).ToList(),
            truncated);
    }

    private async Task<(int Processed, int Succeeded, int Failed, string? StopReason)> RefreshManyAsync(
        List<string> seriesNames,
        Func<int, int, int, int, Task> progressAction)
    {
        return await RunBulkAsync(seriesNames, progressAction, async name =>
        {
            var row = await _seriesRepository.GetByNameWithExpectedBooksAsync(name);
            if (row is null || string.IsNullOrEmpty(row.MatchedSourceName) || string.IsNullOrEmpty(row.MatchedSourceId))
            {
                _logger.LogInformation("Skipping refresh for unmatched series {SeriesName}", name);
                return false;
            }

            await RefreshOneSeriesCoreAsync(name, row);
            return true;
        });
    }

    /// <summary>
    /// The one-series refresh workload shared by the synchronous single refresh and the bulk
    /// sweep: re-fetch the roster from the matched source, store it (stamping
    /// <c>LastRefreshedAt</c> - the user's ignore decisions survive automatically because
    /// UpsertAsync refreshes rows in place), then diff the fresh roster against the series' owned
    /// books and keep the pending snapshot in step - upserted when the refresh found explicit
    /// changes, deleted when it found none, so a no-change bulk item never appears in the pending
    /// list.
    /// </summary>
    private async Task<(bool HasChanges, int ChangeCount, string? SourceName)> RefreshOneSeriesCoreAsync(
        string seriesName, Series row)
    {
        var scraper = SeriesCapableScrapers.FirstOrDefault(s => s.IsSource(row.MatchedSourceName!))
            ?? throw new ArgumentException($"No series-capable scraper for source {row.MatchedSourceName}");

        var roster = await scraper.GetSeriesBooks(row.MatchedSourceId!)
            ?? throw new Exception($"Source {row.MatchedSourceName} returned no series for id {row.MatchedSourceId}");

        // The roster is the stored source page (capped by the same bound the reconciliation
        // enforces), and the owned-key fetch is bounded too, so the diff is computed against
        // explicitly limited inputs - it never materializes the whole owned set of a series.
        var (ownedKeys, ownedOverflow) = await _audiobookRepository.GetSeriesOwnedKeysAsync(
            seriesName, SeriesReconciliationProvider.MaxReconciliationOwnedKeys);
        if (ownedOverflow)
        {
            throw new InvalidOperationException(
                $"Series '{seriesName}' has at least {SeriesReconciliationProvider.MaxReconciliationOwnedKeys + 1} owned books, exceeding the {SeriesReconciliationProvider.MaxReconciliationOwnedKeys} the refresh diffs against.");
        }

        var changes = SeriesRefreshDiffer.Diff(
            ToRosterEntries(roster.Books),
            ownedKeys,
            includeOmnibusEditions: row.IncludeOmnibusEditions);

        await MatchSeriesCoreAsync(
            seriesName, row.MatchedSourceName!, row.MatchedSourceId!,
            row.MatchConfidence, row.IncludeOmnibusEditions, roster);

        await PersistPendingChangesAsync(seriesName, scraper.SourceName, roster, changes);

        // The result's SourceName is the scraper/source name (e.g. "Hardcover"), the same value
        // the pending snapshot and catalog row carry as SourceName - never the source's own
        // series title, which is a separate piece of data (SourceSeriesName on the payload, the
        // catalog row's MatchedSeriesName).
        var hasChanges = changes.Count > 0 || HasAdoptableName(seriesName, roster.SeriesName);
        return (hasChanges, changes.Count, scraper.SourceName);
    }

    /// <summary>
    /// A roster whose source title differs from the stored series name is itself a pending
    /// state even when the diff found no book-level changes: the review dialog's "adopt the
    /// source name" action is only reachable through a pending snapshot, so a series renamed
    /// locally before matching would otherwise never be offered the alignment again. The
    /// comparison mirrors the dialog's own check (trimmed, ordinal) so both sides agree on
    /// when the option appears.
    /// </summary>
    private static bool HasAdoptableName(string seriesName, string? sourceSeriesName) =>
        !string.IsNullOrWhiteSpace(sourceSeriesName) &&
        !string.Equals(sourceSeriesName.Trim(), seriesName.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// Stores the pending snapshot for a refreshed series, or clears any stale one when the
    /// refresh found no changes and the source title agrees with the stored name. This is the
    /// "no-change bulk items are omitted" guarantee's write side: the pending list is only
    /// ever populated from here, and only rows with something to review are written - which
    /// includes a name alignment with no book-level changes.
    /// </summary>
    private async Task PersistPendingChangesAsync(
        string seriesName, string sourceName, SeriesSearchResult roster, IReadOnlyList<SeriesRefreshChange> changes)
    {
        if (changes.Count == 0 && !HasAdoptableName(seriesName, roster.SeriesName))
        {
            await _pendingSeriesRefreshRepository.DeleteBySeriesNameAsync(seriesName);
            return;
        }

        var pending = new PendingSeriesRefresh(
            seriesName,
            DateTime.UtcNow,
            sourceName,
            roster.SourceUrl ?? string.Empty,
            string.IsNullOrWhiteSpace(roster.SeriesName) ? null : roster.SeriesName,
            changes,
            ToRosterEntries(roster.Books));

        await _pendingSeriesRefreshRepository.UpsertAsync(new Database.Models.PendingSeriesRefresh
        {
            SeriesName = pending.SeriesName,
            FetchedAt = pending.FetchedAt,
            SourceName = pending.SourceName,
            SourceUrl = pending.SourceUrl,
            PayloadJson = PendingSeriesRefreshPayload.Serialize(ToPayload(pending)),
        });
    }

    public async Task<PendingSeriesRefresh?> GetPendingSeriesRefreshAsync(string seriesName)
    {
        var row = await _pendingSeriesRefreshRepository.GetBySeriesNameAsync(seriesName);
        if (row is null)
        {
            return null;
        }

        var payload = PendingSeriesRefreshPayload.TryParse(row.PayloadJson);
        return payload is null ? null : ToDomain(payload);
    }

    public async Task<(List<PendingSeriesRefreshListItem> Items, int Total)> GetPendingSeriesRefreshPageAsync(int page, int pageSize)
    {
        var (rows, total) = await _pendingSeriesRefreshRepository.GetPageAsync(page * pageSize, pageSize);

        var items = new List<PendingSeriesRefreshListItem>();
        foreach (var row in rows)
        {
            var parsed = PendingSeriesRefreshPayload.TryParse(row.PayloadJson);
            items.Add(new PendingSeriesRefreshListItem(
                row.SeriesName,
                row.SourceName,
                parsed?.SourceSeriesName,
                row.FetchedAt,
                parsed?.Changes.Count ?? 0));
        }

        return (items, total);
    }

    public Task<int> CountPendingSeriesRefreshesAsync() =>
        _pendingSeriesRefreshRepository.CountAsync();

    public async Task<bool> DismissPendingSeriesRefreshAsync(string seriesName)
    {
        // A dismissed (or already-applied) snapshot is simply absent from the pending list.
        // DeleteBySeriesNameAsync returns whether a row actually existed - the idempotent
        // success the dismiss UX wants either way.
        return await _pendingSeriesRefreshRepository.DeleteBySeriesNameAsync(seriesName);
    }

    /// <summary>
    /// Applies the accepted selections of a pending series refresh, and optionally adopts the
    /// source's own series name across every member book. Each applied change rewrites its book
    /// through <see cref="IAudiobookService.UpdateAudiobook"/> under the per-audiobook save gate
    /// - the same pipeline the interactive edit uses, so tags, path, sidecars and database stay
    /// in step (the "no DB-only field updates" binding invariant). After the batch, the pending
    /// snapshot is recomputed against the stored roster and the now-current owned books: a
    /// series whose changes are all resolved drops out of the pending list, one with leftovers
    /// keeps a snapshot containing exactly them.
    ///
    /// The batch contract mirrors the other bulk rewrites: one try/catch per selection so a busy
    /// or missing book fails just its own item and the rest carry on, plus a (processed, total,
    /// succeeded, failed) progress report after every item. The optional source-series-name
    /// adoption is one item of that total: it renames every member book, migrates the matched
    /// catalog row (roster, ignore flags and all matched metadata) to the adopted name, and any
    /// failure inside it fails just that item. The returned effective series name tells the
    /// caller that a fully successful adoption moved the series: it is the adopted name on
    /// success and null otherwise, so the client can navigate its route to the new name.
    /// </summary>
    public async Task<(int Processed, int Succeeded, int Failed, string? EffectiveSeriesName)> ApplyPendingSeriesRefreshAsync(
        string seriesName,
        SeriesRefreshApplyRequest request,
        Func<int, int, int, int, Task> progressAction)
    {
        var row = await _pendingSeriesRefreshRepository.GetBySeriesNameAsync(seriesName)
            ?? throw new KeyNotFoundException($"No pending series refresh exists for '{seriesName}'.");

        var payload = PendingSeriesRefreshPayload.TryParse(row.PayloadJson);
        if (payload is null)
        {
            // A foreign/corrupt payload is not reviewable; drop it and let the caller see the
            // same "nothing pending" state a missing row would produce.
            await _pendingSeriesRefreshRepository.DeleteBySeriesNameAsync(seriesName);
            throw new KeyNotFoundException($"No pending series refresh exists for '{seriesName}'.");
        }

        var pending = ToDomain(payload);
        var catalog = await _seriesRepository.GetByNameAsync(seriesName);
        var includeOmnibusEditions = catalog?.IncludeOmnibusEditions ?? false;

        // The adoption is one item of the batch's total only when it will actually run. The source
        // name is trimmed before it is compared and before it is adopted, matching the review
        // dialog's own gate on the checkbox - and keeping a series from being renamed to a value
        // with leading or trailing whitespace.
        var adoptedSourceName = payload.SourceSeriesName?.Trim();
        var adoptionSelected = request.AdoptSourceSeriesName
            && !string.IsNullOrEmpty(adoptedSourceName)
            && adoptedSourceName != seriesName;

        // Counting request.AdoptSourceSeriesName here instead would overstate the total whenever
        // the request asks to adopt a name that is blank or already the series' own: nothing is
        // processed for it, so the progress bar stopped one short of its total and never reached
        // 100%.
        var total = request.Selections.Count + (adoptionSelected ? 1 : 0);
        var processed = 0;
        var succeeded = 0;
        var failed = 0;

        foreach (var selection in request.Selections)
        {
            processed++;
            try
            {
                await ApplySingleSeriesRefreshChangeAsync(seriesName, pending, selection);
                succeeded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Applying series refresh change {ChangeType} (audiobook {AudiobookId}) for series {SeriesName} failed",
                    selection.Type, selection.AudiobookId, seriesName);
                failed++;
            }

            await progressAction(processed, total, succeeded, failed);
        }

        // Adoption runs after the accepted changes on purpose: a missing-book selection assigned
        // during this apply still lands on the NEW series value via the rename - the final value
        // of every member book is the adopted name regardless of when it joined the series. Only a
        // FULLY successful adoption flips the effective series name; a partial failure leaves books
        // on both names, and the pending state stays addressable under the original name so the
        // user can retry.
        var adoptedName = (string?)null;
        if (adoptionSelected)
        {
            processed++;
            try
            {
                await AdoptSourceSeriesNameAsync(seriesName, adoptedSourceName!);
                adoptedName = adoptedSourceName;
                succeeded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Adopting source series name '{NewName}' for series {SeriesName} failed", adoptedSourceName, seriesName);
                failed++;
            }

            await progressAction(processed, total, succeeded, failed);
        }

        // The roster the user reviewed is the apply's source of truth (re-fetching would apply
        // whatever the source says NOW, and an HTTP failure would block the apply). Recompute the
        // remaining changes against the stored roster and the books as they are after the batch,
        // under the EFFECTIVE series name: the adopted name when the adoption fully succeeded (a
        // fully adopted series has no books under the old name, so re-checking the old name would
        // report the whole roster as missing and leave a bogus pending row under a name nobody
        // owns), the original name otherwise - including a partial adoption failure, where the old
        // name is the addressable, retryable state.
        var effectiveSeriesName = adoptedName ?? seriesName;
        var (freshOwnedKeys, freshOverflow) = await _audiobookRepository.GetSeriesOwnedKeysAsync(
            effectiveSeriesName, SeriesReconciliationProvider.MaxReconciliationOwnedKeys);
        if (freshOverflow)
        {
            // A pathological owned set cannot be diffed safely. Fabricating an "everything is
            // resolved" outcome would falsely delete the pending snapshot; retaining it untouched
            // keeps the apply retryable. The accepted changes were already written, and re-applying
            // them is idempotent, so the (now stale) change list is the right failure mode.
            _logger.LogWarning(
                "Series '{SeriesName}' has at least {OwnedCount} owned books; keeping the pending refresh snapshot instead of recomputing it after the apply",
                effectiveSeriesName, SeriesReconciliationProvider.MaxReconciliationOwnedKeys + 1);
            return (processed, succeeded, failed, adoptedName);
        }

        var remaining = SeriesRefreshDiffer.Diff(
            pending.Roster,
            freshOwnedKeys,
            includeOmnibusEditions).ToList();

        // The pending row is always removed from the ORIGINAL name on this path: a fully adopted
        // series has no books under it anymore, and a non-adopted series either resolved all its
        // changes or keeps its snapshot under that same name (upserted just below).
        if (adoptedName is not null)
        {
            await _pendingSeriesRefreshRepository.DeleteBySeriesNameAsync(seriesName);
        }

        // The recompute mirrors PersistPendingChangesAsync's keep-rule: a snapshot that exists
        // only for the source-name alignment (no book-level changes) must survive an apply that
        // did not adopt (or adopted partially) - the mismatch persists, and deleting it would
        // make the alignment option unreachable until the next refresh.
        if (remaining.Count == 0 && !HasAdoptableName(effectiveSeriesName, payload.SourceSeriesName))
        {
            await _pendingSeriesRefreshRepository.DeleteBySeriesNameAsync(effectiveSeriesName);
        }
        else if (remaining.Count == 0)
        {
            await _pendingSeriesRefreshRepository.UpsertAsync(new Database.Models.PendingSeriesRefresh
            {
                SeriesName = effectiveSeriesName,
                FetchedAt = row.FetchedAt,
                SourceName = row.SourceName,
                SourceUrl = row.SourceUrl,
                PayloadJson = PendingSeriesRefreshPayload.Serialize(ToPayload(
                    new PendingSeriesRefresh(
                        effectiveSeriesName,
                        row.FetchedAt,
                        row.SourceName,
                        row.SourceUrl,
                        payload.SourceSeriesName,
                        remaining,
                        pending.Roster))),
            });
        }
        else
        {
            await _pendingSeriesRefreshRepository.UpsertAsync(new Database.Models.PendingSeriesRefresh
            {
                SeriesName = effectiveSeriesName,
                FetchedAt = row.FetchedAt,
                SourceName = row.SourceName,
                SourceUrl = row.SourceUrl,
                PayloadJson = PendingSeriesRefreshPayload.Serialize(ToPayload(
                    new PendingSeriesRefresh(
                        effectiveSeriesName,
                        row.FetchedAt,
                        row.SourceName,
                        row.SourceUrl,
                        payload.SourceSeriesName,
                        remaining,
                        pending.Roster))),
            });
        }

        // Every accepted change rewrote an owned book's Series/SeriesPart; the rename changes all
        // of them. The cached reconciliations for both names (when they differ) are stale - and on
        // a partial adoption failure both names genuinely changed.
        _reconciliationCache.Invalidate(seriesName);
        if (adoptionSelected)
        {
            _reconciliationCache.Invalidate(adoptedSourceName!);
        }

        return (processed, succeeded, failed, adoptedName);
    }

    /// <summary>
    /// Deletes a series: clears Series/SeriesPart on every owned book (through
    /// <see cref="IAudiobookService.UpdateAudiobook"/>, under the per-audiobook save gate, with
    /// a best-effort consistency recheck per book - the same pipeline and tail every other
    /// series/book rewrite in this service uses), then removes the catalog row and any pending
    /// refresh snapshot. The catalog cleanup runs even when some books failed to clear: a
    /// half-deleted series should not leave a matched catalog row (with its roster and mapping
    /// patterns) behind for a book re-added under the same name to inherit stale "missing book"
    /// state against.
    /// </summary>
    public async Task<(int Processed, int Succeeded, int Failed)> DeleteSeriesAsync(
        string seriesName,
        Func<int, int, int, int, Task> progressAction)
    {
        var ownedBooks = (await _audiobookRepository.GetBooksBySeriesAsync(seriesName, authorId: null))
            .Select(AudiobookService.FromDb)
            .ToList();

        var result = await BulkOperationRunner.RunAsync(
            ownedBooks,
            async book =>
            {
                var audiobookId = book.Id!.Value;
                using var lease = _saveGate.Acquire(audiobookId);
                book.Series = string.Empty;
                book.SeriesPart = null;
                await _audiobookService.UpdateAudiobook(audiobookId, book);

                try
                {
                    await _libraryConsistencyService.RecheckAudiobookAsync(audiobookId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to recheck consistency issues for audiobook {AudiobookId} after clearing its series on series deletion",
                        audiobookId);
                }
            },
            _logger,
            book => $"Failed to clear series for audiobook {book.Id} while deleting series {seriesName}",
            progressAction);

        await _pendingSeriesRefreshRepository.DeleteBySeriesNameAsync(seriesName);
        await _seriesRepository.DeleteSeriesAsync(seriesName);
        _reconciliationCache.Invalidate(seriesName);

        // Every owned book just had its Series value rewritten to empty - exactly the kind of
        // bulk series-value change AlignSeriesAsync invalidates this cache for. Without it, a
        // similar-values group naming the now-deleted series (matching only the books that
        // failed to clear, if any) can be served stale until the TTL expires.
        _similarValueDetectionCache.Invalidate();

        return result;
    }

    private async Task ApplySingleSeriesRefreshChangeAsync(
        string seriesName,
        PendingSeriesRefresh pending,
        SeriesRefreshApplyChange selection)
    {
        var change = FindPendingChange(pending, selection)
            ?? throw new InvalidOperationException(
                $"Pending change {selection.Type} (audiobook {selection.AudiobookId}, position '{selection.Position}', title '{selection.Title}') not found in the stored snapshot.");

        var audiobookId = selection.AudiobookId
            ?? throw new InvalidOperationException("An accepted series refresh change must address a library audiobook.");

        var audiobook = await _audiobookService.GetAudiobookById(audiobookId)
            ?? throw new KeyNotFoundException($"Audiobook {audiobookId} not found");

        switch (selection.Type)
        {
            case SeriesRefreshChangeType.PartUpdate:
                audiobook.Series = seriesName;
                audiobook.SeriesPart = change.NewPart;
                break;
            case SeriesRefreshChangeType.PartRemoval:
                audiobook.Series = seriesName;
                audiobook.SeriesPart = null;
                break;
            case SeriesRefreshChangeType.MissingBook:
                audiobook.Series = seriesName;
                audiobook.SeriesPart = change.Position;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        // One library book is never rewritten by two writers concurrently; a book a save or
        // another batch is already touching fails just this item and the batch carries on. The
        // follow-up consistency recheck mirrors the interactive apply's tail, run inside the
        // lease the same way: the rewrite moved tags and possibly the file, so stored issues for
        // this book are stale until rechecked, and a recheck failure is best-effort - it must not
        // turn a successful rewrite into a failed item.
        using var lease = _saveGate.Acquire(audiobookId);
        await _audiobookService.UpdateAudiobook(audiobookId, audiobook);
        try
        {
            await _libraryConsistencyService.RecheckAudiobookAsync(audiobookId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to recheck consistency issues for audiobook {AudiobookId} after a pending series refresh change", audiobookId);
        }
    }

    /// <summary>
    /// Matches an accepted selection to the stored change it acts on. Part updates and removals
    /// are addressed by the target audiobook id; a missing book is addressed by its roster
    /// natural key (position and/or title, trimmed and case-insensitive), the same way the
    /// expected-book endpoints address roster entries. Anything else is "not found" and fails
    /// just its own item.
    /// </summary>
    private static SeriesRefreshChange? FindPendingChange(
        PendingSeriesRefresh pending,
        SeriesRefreshApplyChange selection)
    {
        foreach (var change in pending.Changes)
        {
            if (change.Type != selection.Type)
            {
                continue;
            }

            if (selection.Type == SeriesRefreshChangeType.MissingBook)
            {
                if (string.Equals(change.Position?.Trim(), selection.Position?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(change.Title?.Trim(), selection.Title?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return change;
                }

                continue;
            }

            if (change.AudiobookId == selection.AudiobookId)
            {
                return change;
            }
        }

        return null;
    }

    /// <summary>
    /// Renames the series value to <paramref name="newName"/> on every member book, one
    /// <see cref="UpdateAudiobook"/> per book under the per-audiobook save gate - opt-in (the
    /// user checks "rename this series to match the source" in the review dialog) and routed
    /// through the normal save pipeline so tags, folders and sidecars move with the rename.
    /// Only the series value changes; parts, names and years are untouched. After the books,
    /// the matched catalog row follows (see <see cref="ISeriesRepository.RenameAsync"/>): the
    /// fully adopted series otherwise keeps a matched zombie row under the old name while the
    /// adopted name owns no roster at all.
    /// </summary>
    private async Task AdoptSourceSeriesNameAsync(string seriesName, string newName)
    {
        // Refuse BEFORE renaming any book when the destination is already a catalog row: a
        // rename would silently merge two rosters (or clobber one), and book renames cannot be
        // unwound. The pre-check is the common case in practice - a stale row under the name
        // books are being renamed onto - and the rename below re-checks the uniqueness anyway.
        var existingCatalog = await _seriesRepository.GetByNameAsync(newName);
        if (existingCatalog is not null)
        {
            throw new InvalidOperationException(
                $"A series named '{newName}' already exists in the catalog; refusing to rename onto it.");
        }

        var (ownedKeys, ownedOverflow) = await _audiobookRepository.GetSeriesOwnedKeysAsync(
            seriesName, SeriesReconciliationProvider.MaxReconciliationOwnedKeys);
        if (ownedOverflow)
        {
            throw new InvalidOperationException(
                $"Series '{seriesName}' has at least {SeriesReconciliationProvider.MaxReconciliationOwnedKeys + 1} owned books, exceeding the {SeriesReconciliationProvider.MaxReconciliationOwnedKeys} an adoption can rename.");
        }

        foreach (var owned in ownedKeys)
        {
            using var lease = _saveGate.Acquire(owned.AudiobookId);
            var audiobook = await _audiobookService.GetAudiobookById(owned.AudiobookId)
                ?? throw new KeyNotFoundException($"Audiobook {owned.AudiobookId} not found");
            audiobook.Series = newName;
            await _audiobookService.UpdateAudiobook(owned.AudiobookId, audiobook);

            // Same best-effort tail as the per-change rewrites: the rename moved tags and
            // possibly the file, so stored issues for this book are stale until rechecked.
            try
            {
                await _libraryConsistencyService.RecheckAudiobookAsync(owned.AudiobookId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to recheck consistency issues for audiobook {AudiobookId} after adopting series name '{NewName}'", owned.AudiobookId, newName);
            }
        }

        // The catalog row follows the books only once every book rename succeeded: a partial
        // adoption failure keeps the catalog under the old name, which is the addressable,
        // retryable state (the pending row stays there too).
        await _seriesRepository.RenameAsync(seriesName, newName);
    }

    private static IReadOnlyList<SeriesRefreshRosterEntry> ToRosterEntries(IEnumerable<SeriesExpectedBookResult> books) =>
        books.Select(b => new SeriesRefreshRosterEntry(
            b.Position,
            b.Title,
            b.Year,
            b.SourceUrl,
            b.IsCompilation)).ToList();

    private static PendingSeriesRefreshPayload.Payload ToPayload(PendingSeriesRefresh pending) =>
        new(
            PendingSeriesRefreshPayload.CurrentVersion,
            pending.SeriesName,
            pending.SourceName,
            pending.SourceUrl,
            pending.SourceSeriesName,
            pending.FetchedAt,
            pending.Roster.Select(e => new PendingSeriesRefreshPayload.RosterEntry(
                e.Position, e.Title, e.Year, e.SourceUrl, e.IsCompilation)).ToList(),
            pending.Changes.Select(ToPayloadChange).ToList());

    private static PendingSeriesRefreshPayload.Change ToPayloadChange(SeriesRefreshChange change) =>
        new(
            change.Type,
            change.AudiobookId,
            change.BookName,
            change.StoredPart,
            change.NewPart,
            change.RosterTitle,
            change.Position,
            change.Title,
            change.Year);

    private static PendingSeriesRefresh ToDomain(PendingSeriesRefreshPayload.Payload payload) =>
        new(
            payload.SeriesName,
            payload.FetchedAt,
            payload.SourceName,
            payload.SourceUrl,
            payload.SourceSeriesName,
            payload.Changes.Select(c => new SeriesRefreshChange(
                c.Type,
                c.AudiobookId,
                c.BookName,
                c.StoredPart,
                c.NewPart,
                c.RosterTitle,
                c.Position,
                c.Title,
                c.Year)).ToList(),
            payload.Roster.Select(e => new SeriesRefreshRosterEntry(
                e.Position, e.Title, e.Year, e.SourceUrl, e.IsCompilation)).ToList());

    /// <summary>
    /// Runs a per-series operation with the shared bulk contract: one try/catch per item so a
    /// single failure never aborts the batch, and a (processed, total, succeeded, failed)
    /// progress report after every item.
    ///
    /// The Hardcover daily request budget is the one exception to "a single failure never
    /// aborts the batch": once it is exhausted every remaining item is guaranteed to fail the
    /// same way, so the batch stops immediately instead of grinding through the rest as
    /// individual failures, and the reason is surfaced on the result rather than folded into
    /// the failed count.
    /// </summary>
    private async Task<(int Processed, int Succeeded, int Failed, string? StopReason)> RunBulkAsync(
        List<string> seriesNames,
        Func<int, int, int, int, Task> progressAction,
        Func<string, Task<bool>> operation)
    {
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var total = seriesNames.Count;
        string? stopReason = null;

        foreach (var name in seriesNames)
        {
            processed++;
            try
            {
                if (await operation(name))
                {
                    succeeded++;
                }
            }
            catch (HardcoverDailyLimitExceededException ex)
            {
                // This item was refused before it was attempted, not failed - don't count it
                // as processed, and don't bother reporting the ones after it.
                processed--;
                _logger.LogWarning(ex,
                    "Stopping series catalog bulk operation after {Processed}/{Total} series: {Message}",
                    processed, total, ex.Message);
                stopReason = "Hardcover daily API request limit reached";
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Series catalog operation failed for series {SeriesName}", name);
                failed++;
            }

            await progressAction(processed, total, succeeded, failed);
        }

        return (processed, succeeded, failed, stopReason);
    }

    private static SeriesOverview BuildOverview(string seriesName, List<SeriesGroupingBook> ownedBooks, Series? catalogRow)
    {
        var includeOmnibusEditions = catalogRow?.IncludeOmnibusEditions ?? false;
        var expected = (catalogRow?.ExpectedBooks ?? new List<ExpectedBook>())
            .Where(e => includeOmnibusEditions || !e.IsCompilation)
            .ToList();
        var active = expected.Where(e => !e.IsIgnored).ToList();
        // The overview index only answers "is this roster entry owned" - never the part-mismatch
        // pass - so the owned books can be reduced to synthetic keys without their row ids.
        var ownedIndex = new SeriesRosterMatcher.OwnedBookIndex(
            ownedBooks.Select(b => new SeriesOwnedKey(0, b.SeriesPart, b.BookName)));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var unmatched = active.Where(e => !SeriesReconciliationProvider.IsOwned(e, ownedIndex)).ToList();

        return BuildOverview(
            seriesName,
            catalogRow,
            ownedBooks
                .SelectMany(b => b.Authors)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ownedBooks.Count,
            active.Count,
            expected.Count - active.Count,
            unmatched.Count(e => !ExpectedBookClassifier.IsUpcoming(e.ReleaseDate, e.Year, today)),
            unmatched.Count(e => ExpectedBookClassifier.IsUpcoming(e.ReleaseDate, e.Year, today)));
    }

    /// <summary>
    /// The detail-path overview: the counts and authors come from the cached reconciliation
    /// rather than a per-book grouping load, so the page renders from bounded data and the badge
    /// counts always agree with the missing/ignored sections they summarize.
    /// </summary>
    private static SeriesOverview BuildReconciledOverview(
        string seriesName, Series? catalogRow, SeriesReconciliation reconciliation) =>
        BuildOverview(
            seriesName,
            catalogRow,
            reconciliation.Authors.ToList(),
            reconciliation.OwnedCount,
            reconciliation.ExpectedBookCount,
            reconciliation.IgnoredBookCount,
            reconciliation.MissingBookCount,
            reconciliation.UpcomingBookCount);

    private static SeriesOverview BuildOverview(
        string seriesName,
        Series? catalogRow,
        List<string> authors,
        int ownedBookCount,
        int expectedBookCount,
        int ignoredBookCount,
        int missingBookCount,
        int upcomingBookCount = 0) =>
        new()
        {
            Id = catalogRow?.Id,
            Name = seriesName,
            Authors = authors,
            OwnedBookCount = ownedBookCount,
            IsMatched = catalogRow is not null
                && !string.IsNullOrEmpty(catalogRow.MatchedSourceName)
                && !string.IsNullOrEmpty(catalogRow.MatchedSourceId),
            MatchedSourceName = catalogRow?.MatchedSourceName,
            MatchedSourceId = catalogRow?.MatchedSourceId,
            MatchedSourceUrl = catalogRow?.MatchedSourceUrl,
            MatchConfidence = catalogRow?.MatchConfidence,
            LastRefreshedAt = catalogRow?.LastRefreshedAt,
            ExpectedBookCount = expectedBookCount,
            IgnoredBookCount = ignoredBookCount,
            MissingBookCount = missingBookCount,
            UpcomingBookCount = upcomingBookCount,
            IncludeOmnibusEditions = catalogRow?.IncludeOmnibusEditions ?? false,
        };

    /// <summary>
    /// 0..1 similarity of two free-text values, using the shared comparison-only normalizer
    /// and edit distance scaled by the longer string's length.
    /// </summary>
    public static double TitleSimilarity(string? a, string? b) =>
        SeriesRosterMatcher.TitleSimilarity(a, b);

    /// <summary>
    /// Scores a source series against the library's series value: mostly name similarity,
    /// nudged up when the source's author overlaps an author of the owned books.
    /// </summary>
    public static double ScoreCandidate(
        string librarySeriesName,
        IReadOnlyCollection<string> libraryAuthors,
        string candidateSeriesName,
        IEnumerable<string> candidateAuthors)
    {
        var nameScore = TitleSimilarity(librarySeriesName, candidateSeriesName);

        var authorList = candidateAuthors?.ToList() ?? new List<string>();
        if (libraryAuthors.Count == 0 || authorList.Count == 0)
        {
            return Math.Round(nameScore, 4);
        }

        var authorOverlap = authorList.Any(ca => libraryAuthors.Any(la => TitleSimilarity(la, ca) >= 0.85));

        // An author match is corroborating evidence, not evidence on its own: it can only
        // close part of the gap to 1, and never rescues a name that doesn't resemble the value.
        var score = authorOverlap ? nameScore + (1 - nameScore) * 0.25 : nameScore * 0.95;

        return Math.Round(Math.Clamp(score, 0, 1), 4);
    }
}
