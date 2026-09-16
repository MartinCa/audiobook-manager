using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Scraping.Scrapers;
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
    /// The largest roster the reconciliation will classify in memory. The roster is stored from a
    /// metadata source's series page, so this is a defensive floor far above any real source
    /// (Hardcover returns one series at a time). It is enforced BEFORE materialization: the
    /// repository fetch is bounded to cap + 1 rows, so a pathological roster is detected and
    /// refused without ever being loaded whole. It is a hard bound on purpose - a series past it
    /// indicates corrupted data, and failing loudly beats showing wrong missing counts.
    /// </summary>
    internal const int MaxReconciliationRosterEntries = 5_000;

    /// <summary>
    /// The largest owned-book key set (per series) the reconciliation will classify against. Same
    /// rationale as <see cref="MaxReconciliationRosterEntries"/>: the fuzzy matching is
    /// O(roster x owned), so both sides must be explicitly bounded for the computation to be
    /// genuinely bounded rather than merely usually-small - and the owned-key fetch is bounded to
    /// cap + 1 rows so an oversized set is detected without materializing it.
    /// </summary>
    internal const int MaxReconciliationOwnedKeys = 20_000;

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly IPendingSeriesRefreshRepository _pendingSeriesRefreshRepository;
    private readonly IAudiobookService _audiobookService;
    private readonly IAudiobookSaveGate _saveGate;
    private readonly ILibraryConsistencyService _libraryConsistencyService;
    private readonly ISeriesReconciliationCache _reconciliationCache;
    private readonly IEnumerable<IScraper> _scrapers;
    private readonly ILogger<SeriesService> _logger;

    public SeriesService(
        IAudiobookRepository audiobookRepository,
        ISeriesRepository seriesRepository,
        IPendingSeriesRefreshRepository pendingSeriesRefreshRepository,
        IAudiobookService audiobookService,
        IAudiobookSaveGate saveGate,
        ILibraryConsistencyService libraryConsistencyService,
        ISeriesReconciliationCache reconciliationCache,
        IEnumerable<IScraper> scrapers,
        ILogger<SeriesService> logger)
    {
        _audiobookRepository = audiobookRepository;
        _seriesRepository = seriesRepository;
        _pendingSeriesRefreshRepository = pendingSeriesRefreshRepository;
        _audiobookService = audiobookService;
        _saveGate = saveGate;
        _libraryConsistencyService = libraryConsistencyService;
        _reconciliationCache = reconciliationCache;
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
        int page, int pageSize, string? search, bool? matched, long? authorId = null)
    {
        var (names, totalCount) = await _audiobookRepository.GetSeriesValuesPageAsync(
            search, matched, skip: (int)((long)page * pageSize), take: pageSize, authorId);

        if (names.Count == 0)
        {
            return new SeriesOverviewPage { Items = new List<SeriesOverview>(), TotalCount = totalCount };
        }

        var booksBySeries = (await _audiobookRepository.GetSeriesGroupingDataAsync(names))
            .ToLookup(b => b.Series, StringComparer.Ordinal);
        var catalogByName = (await _seriesRepository.GetByNamesWithExpectedBooksAsync(names))
            .ToDictionary(s => s.Name, StringComparer.Ordinal);

        var items = names.Select(name =>
        {
            catalogByName.TryGetValue(name, out var catalogRow);
            return BuildOverview(name, booksBySeries[name].ToList(), catalogRow);
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
        int ignoredSkip, int ignoredTake,
        int partMismatchSkip, int partMismatchTake)
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
            IgnoredBooks = reconciliation.Ignored.Skip(ignoredSkip).Take(ignoredTake).ToList(),
            IgnoredBookTotal = reconciliation.Ignored.Count,
            PartMismatches = reconciliation.PartMismatches.Skip(partMismatchSkip).Take(partMismatchTake).ToList(),
            PartMismatchTotal = reconciliation.PartMismatchCount,
        };
    }

    /// <summary>
    /// The series' reconciliation for the whole library-wide surface (the detail page's sections
    /// and the consistency detector both consume this cached view), computed once per series per
    /// change. The computation classifies the full roster against the series' owned (id, position,
    /// title) keys - the fuzzy ownership semantics demand the whole key set, which SQL cannot
    /// express - so it is the one step that touches per-series data beyond the requested page, and
    /// it is bounded: the roster is the metadata source's stored series page and both inputs sit
    /// under the <see cref="MaxReconciliationRosterEntries"/>/<see cref="MaxReconciliationOwnedKeys"/>
    /// caps. The single-flight gate, the version check and the capacity-bounded eviction all live
    /// inside <see cref="ISeriesReconciliationCache"/>, so a page-flip stampede shares one
    /// computation and the cache cannot grow with the library.
    /// </summary>
    public Task<SeriesReconciliation> GetReconciliationAsync(string seriesName) =>
        _reconciliationCache.GetOrComputeAsync(seriesName, () => ComputeReconciliationAsync(seriesName));

    private async Task<SeriesReconciliation> ComputeReconciliationAsync(string seriesName)
    {
        // Both inputs are read through bounded repository queries (cap + 1 rows with an overflow
        // flag), so a pathological roster or owned set is detected and refused BEFORE it is ever
        // fully materialized or transferred - the service never sees the unbounded collection.
        var (catalogRow, rosterOverflow) = await _seriesRepository.GetByNameWithExpectedBooksBoundedAsync(
            seriesName, MaxReconciliationRosterEntries);
        if (rosterOverflow)
        {
            throw new InvalidOperationException(
                $"Series '{seriesName}' has at least {MaxReconciliationRosterEntries + 1} roster entries, exceeding the {MaxReconciliationRosterEntries} the detail view reconciles.");
        }

        var expected = catalogRow?.ExpectedBooks ?? new List<SeriesExpectedBook>();

        var (ownedKeys, ownedOverflow) = await _audiobookRepository.GetSeriesOwnedKeysAsync(
            seriesName, MaxReconciliationOwnedKeys);
        if (ownedOverflow)
        {
            throw new InvalidOperationException(
                $"Series '{seriesName}' has at least {MaxReconciliationOwnedKeys + 1} owned books, exceeding the {MaxReconciliationOwnedKeys} the detail view reconciles.");
        }

        var includeOmnibusEditions = catalogRow?.IncludeOmnibusEditions ?? false;
        var visible = expected
            .Where(e => includeOmnibusEditions || !e.IsCompilation)
            .ToList();
        var active = visible.Where(e => !e.IsIgnored).ToList();

        var ownedIndex = new SeriesRosterMatcher.OwnedBookIndex(ownedKeys);
        var missing = active
            .Where(e => !IsOwned(e, ownedIndex))
            .Select(ToExpectedInfo)
            .OrderBy(e => SeriesRosterMatcher.PositionSortKey(e.Position))
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

        var ignored = visible
            .Where(e => e.IsIgnored)
            .Select(ToExpectedInfo)
            .OrderBy(e => SeriesRosterMatcher.PositionSortKey(e.Position))
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

        // Owned books that matched a roster entry but carry no part - or a part the roster does
        // not assign to that entry - are not missing (the book is there), they are mislabeled.
        // A mismatch needs a roster-assigned position to fix against: an entry with no position
        // defines nothing to differ from, and a hand-typed part under it is not evidence to clear
        // the book on.
        //
        // Attribution is per book over ALL the entries it matches, not per entry over the books
        // it matches. A book can match several entries (a title repeated across positions - the
        // omnibus and individual editions of one book), and the first entry in display order is
        // not necessarily the right one to judge it against: a stored part that agrees with any
        // of the book's matched entries is correct, however many title-only sibling entries
        // disagree with it - flagging it there would misassign the expected part. Only a book
        // whose part agrees with NO matched entry is a genuine mismatch, and it is then reported
        // against the earliest-position match, the entry the display-order iteration reaches
        // first. The parts are compared with the same equivalence the matching uses, so "2" vs
        // "2.0" is not a mismatch while "2" vs "" (or "7") is.
        var entries = active
            .Where(e => !string.IsNullOrWhiteSpace(e.Position))
            .OrderBy(e => SeriesRosterMatcher.PositionSortKey(e.Position))
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

        var matchesByBook = new Dictionary<long, List<SeriesExpectedBook>>();
        foreach (var entry in entries)
        {
            foreach (var owned in ownedIndex.FindMatches(SeriesRosterMatcher.BookKey.From(entry.Position, entry.Title)))
            {
                if (!matchesByBook.TryGetValue(owned.AudiobookId, out var bookEntries))
                {
                    bookEntries = new List<SeriesExpectedBook>();
                    matchesByBook[owned.AudiobookId] = bookEntries;
                }

                bookEntries.Add(entry);
            }
        }

        var ownedKeyById = ownedKeys.ToDictionary(k => k.AudiobookId);
        var partMismatches = new List<SeriesPartMismatch>();
        foreach (var audiobookId in matchesByBook.Keys)
        {
            if (!ownedKeyById.TryGetValue(audiobookId, out var owned))
            {
                continue;
            }

            if (matchesByBook[audiobookId].Any(e => SeriesRosterMatcher.PartsEquivalent(owned.SeriesPart, e.Position)))
            {
                continue;
            }

            var attributed = matchesByBook[audiobookId].First();
            partMismatches.Add(new SeriesPartMismatch
            {
                AudiobookId = owned.AudiobookId,
                BookName = owned.BookName,
                StoredPart = owned.SeriesPart,
                ExpectedPart = attributed.Position!,
                RosterTitle = attributed.Title,
            });
        }

        var authors = await _audiobookRepository.GetAuthorNamesBySeriesAsync(seriesName);

        return new SeriesReconciliation(
            missing,
            ignored,
            partMismatches
                .OrderBy(m => SeriesRosterMatcher.PositionSortKey(m.ExpectedPart))
                .ThenBy(m => m.BookName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.AudiobookId)
                .ToList(),
            ExpectedBookCount: active.Count,
            OwnedCount: ownedKeys.Count,
            authors);
    }

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

    /// <summary>
    /// Matches the series to a source and replaces its stored roster, returning the catalog
    /// row. Does no read-side projection work. A caller that already fetched the roster (the
    /// refresh path needs it to compute its diff) passes it in via <paramref name="fetched"/>
    /// so the source is not hit twice.
    /// </summary>
    private async Task<Series> MatchSeriesCoreAsync(
        string seriesName,
        string sourceName,
        string sourceSeriesId,
        double? confidence,
        bool includeOmnibusEditions,
        Series? existingRow = null,
        SeriesSearchResult? fetched = null)
    {
        var scraper = SeriesCapableScrapers.FirstOrDefault(s => s.IsSource(sourceName))
            ?? throw new ArgumentException($"No series-capable scraper for source {sourceName}");

        var roster = fetched
            ?? await scraper.GetSeriesBooks(sourceSeriesId)
            ?? throw new Exception($"Source {sourceName} returned no series for id {sourceSeriesId}");

        // A caller that already read this row (RefreshManyAsync checks it is matched before
        // getting here) passes it in, so refreshing N series costs N reads rather than 2N -
        // each one pulling a full roster.
        var existing = existingRow ?? await _seriesRepository.GetByNameWithExpectedBooksAsync(seriesName);

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

        // Normalize the previously-ignored titles once rather than once per roster entry.
        var previouslyIgnored = (existing?.ExpectedBooks ?? new List<SeriesExpectedBook>())
            .Where(p => p.IsIgnored)
            .Select(p => SeriesRosterMatcher.BookKey.From(p.Position, p.Title))
            .ToList();

        // The full roster is always stored, compilations included - IncludeOmnibusEditions only
        // controls what SeriesService treats as visible when reading it back, so toggling it
        // later doesn't require re-fetching from the source.
        var newExpected = roster.Books.Select(b =>
        {
            var key = SeriesRosterMatcher.BookKey.From(b.Position, b.Title);
            return new SeriesExpectedBook
            {
                Position = b.Position,
                Title = b.Title,
                Year = b.Year,
                SourceUrl = b.SourceUrl,
                IsCompilation = b.IsCompilation,
                // Re-matching or refreshing replaces the roster wholesale, so carry the user's
                // ignore decisions across for entries that are recognisably the same book.
                IsIgnored = previouslyIgnored.Any(p => SeriesRosterMatcher.IsSameBook(p, key)),
            };
        }).ToList();

        await _seriesRepository.ReplaceExpectedBooksAsync(saved.Id, newExpected);

        // The roster was just replaced wholesale - the reconciled detail is stale by definition.
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
        // Loaded WITH the roster (GetByNameWithExpectedBooksAsync), the same shape the bulk
        // refresh reads: MatchSeriesCoreAsync re-stores the roster wholesale and carries the
        // user's ignore decisions across from the row passed in, so a roster-less row would
        // silently clear every ignored entry on the next single-series refresh.
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
        await _seriesRepository.SetExpectedBookIgnoredAsync(seriesName, position, title, ignored);
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
        return book is null ? null : ToExpectedInfo(book);
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
        audiobook.SeriesPart = expected.Position;

        await _audiobookService.UpdateAudiobook(audiobookId, audiobook);
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
    /// <c>LastRefreshedAt</c> and carrying ignore decisions across like a re-match), then diff
    /// the fresh roster against the series' owned books and keep the pending snapshot in step -
    /// upserted when the refresh found explicit changes, deleted when it found none, so a
    /// no-change bulk item never appears in the pending list.
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
            seriesName, MaxReconciliationOwnedKeys);
        if (ownedOverflow)
        {
            throw new InvalidOperationException(
                $"Series '{seriesName}' has at least {MaxReconciliationOwnedKeys + 1} owned books, exceeding the {MaxReconciliationOwnedKeys} the refresh diffs against.");
        }

        var changes = SeriesRefreshDiffer.Diff(
            ToRosterEntries(roster.Books),
            ownedKeys,
            includeOmnibusEditions: row.IncludeOmnibusEditions,
            // The entries the user has already ignored are deliberately NOT part of the review:
            // they are excluded from the visible series on the detail page, so re-reporting them
            // as missing here would contradict that handling and re-litigate the same decision on
            // every refresh. The same-book rule the roster replace uses to carry the flags across
            // is what exempts them here, so a source-renumbered (but recognisably the same) entry
            // stays quiet too.
            previouslyIgnored: (row.ExpectedBooks ?? new List<SeriesExpectedBook>())
                .Where(p => p.IsIgnored)
                .Select(p => SeriesRosterMatcher.BookKey.From(p.Position, p.Title))
                .ToList());

        await MatchSeriesCoreAsync(
            seriesName, row.MatchedSourceName!, row.MatchedSourceId!,
            row.MatchConfidence, row.IncludeOmnibusEditions, row, roster);

        await PersistPendingChangesAsync(seriesName, scraper.SourceName, roster, changes);

        // The result's SourceName is the scraper/source name (e.g. "Hardcover"), the same value
        // the pending snapshot and catalog row carry as SourceName - never the source's own
        // series title, which is a separate piece of data (SourceSeriesName on the payload, the
        // catalog row's MatchedSeriesName).
        return (changes.Count > 0, changes.Count, scraper.SourceName);
    }

    /// <summary>
    /// Stores the pending snapshot for a refreshed series, or clears any stale one when the
    /// refresh found no changes. This is the "no-change bulk items are omitted" guarantee's
    /// write side: the pending list is only ever populated from here, and only rows with
    /// changes are written.
    /// </summary>
    private async Task PersistPendingChangesAsync(
        string seriesName, string sourceName, SeriesSearchResult roster, IReadOnlyList<SeriesRefreshChange> changes)
    {
        if (changes.Count == 0)
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
    /// failure inside it fails just that item.
    /// </summary>
    public async Task<(int Processed, int Succeeded, int Failed)> ApplyPendingSeriesRefreshAsync(
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

        var total = request.Selections.Count + (request.AdoptSourceSeriesName ? 1 : 0);
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
        var adoptionSelected = request.AdoptSourceSeriesName
            && !string.IsNullOrWhiteSpace(payload.SourceSeriesName)
            && payload.SourceSeriesName != seriesName;
        if (adoptionSelected)
        {
            processed++;
            try
            {
                await AdoptSourceSeriesNameAsync(seriesName, payload.SourceSeriesName!);
                adoptedName = payload.SourceSeriesName;
                succeeded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Adopting source series name '{NewName}' for series {SeriesName} failed", payload.SourceSeriesName, seriesName);
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
            effectiveSeriesName, MaxReconciliationOwnedKeys);
        if (freshOverflow)
        {
            // A pathological owned set cannot be diffed safely. Fabricating an "everything is
            // resolved" outcome would falsely delete the pending snapshot; retaining it untouched
            // keeps the apply retryable. The accepted changes were already written, and re-applying
            // them is idempotent, so the (now stale) change list is the right failure mode.
            _logger.LogWarning(
                "Series '{SeriesName}' has at least {OwnedCount} owned books; keeping the pending refresh snapshot instead of recomputing it after the apply",
                effectiveSeriesName, MaxReconciliationOwnedKeys + 1);
            return (processed, succeeded, failed);
        }

        var remaining = SeriesRefreshDiffer.Diff(
            pending.Roster,
            freshOwnedKeys,
            includeOmnibusEditions,
            // Same exemption the refresh applies: an entry the user has already ignored must not
            // re-enter the pending review here either, or it would come back the moment the
            // apply recomputes the snapshot. The authoritative flags live on the stored roster
            // under the effective name, where the refresh carried them (and adoption has since
            // moved them, if it succeeded).
            previouslyIgnored: ((await _seriesRepository.GetByNameWithExpectedBooksAsync(effectiveSeriesName))
                    ?.ExpectedBooks ?? new List<SeriesExpectedBook>())
                .Where(p => p.IsIgnored)
                .Select(p => SeriesRosterMatcher.BookKey.From(p.Position, p.Title))
                .ToList()).ToList();

        // The pending row is always removed from the ORIGINAL name on this path: a fully adopted
        // series has no books under it anymore, and a non-adopted series either resolved all its
        // changes or keeps its snapshot under that same name (upserted just below).
        if (adoptedName is not null)
        {
            await _pendingSeriesRefreshRepository.DeleteBySeriesNameAsync(seriesName);
        }

        if (remaining.Count == 0)
        {
            await _pendingSeriesRefreshRepository.DeleteBySeriesNameAsync(effectiveSeriesName);
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
            _reconciliationCache.Invalidate(payload.SourceSeriesName!);
        }

        return (processed, succeeded, failed);
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
            seriesName, MaxReconciliationOwnedKeys);
        if (ownedOverflow)
        {
            throw new InvalidOperationException(
                $"Series '{seriesName}' has at least {MaxReconciliationOwnedKeys + 1} owned books, exceeding the {MaxReconciliationOwnedKeys} an adoption can rename.");
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
        var expected = (catalogRow?.ExpectedBooks ?? new List<SeriesExpectedBook>())
            .Where(e => includeOmnibusEditions || !e.IsCompilation)
            .ToList();
        var active = expected.Where(e => !e.IsIgnored).ToList();
        // The overview index only answers "is this roster entry owned" - never the part-mismatch
        // pass - so the owned books can be reduced to synthetic keys without their row ids.
        var ownedIndex = new SeriesRosterMatcher.OwnedBookIndex(
            ownedBooks.Select(b => new SeriesOwnedKey(0, b.SeriesPart, b.BookName)));

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
            active.Count(e => !IsOwned(e, ownedIndex)));
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
            reconciliation.MissingBookCount);

    private static SeriesOverview BuildOverview(
        string seriesName,
        Series? catalogRow,
        List<string> authors,
        int ownedBookCount,
        int expectedBookCount,
        int ignoredBookCount,
        int missingBookCount) =>
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
            IncludeOmnibusEditions = catalogRow?.IncludeOmnibusEditions ?? false,
        };

    private static SeriesExpectedBookInfo ToExpectedInfo(SeriesExpectedBook book) => new()
    {
        Id = book.Id,
        Title = book.Title,
        Position = book.Position,
        Year = book.Year,
        SourceUrl = book.SourceUrl,
        IsIgnored = book.IsIgnored,
    };

    /// <summary>
    /// Whether any owned book corresponds to this roster entry. Owned book names rarely match
    /// a source title byte-for-byte, so an exact position match counts, and otherwise titles
    /// are compared fuzzily - the shared rule in <see cref="SeriesRosterMatcher"/>.
    /// </summary>
    private static bool IsOwned(SeriesExpectedBook expected, SeriesRosterMatcher.OwnedBookIndex ownedBooks) =>
        ownedBooks.Contains(SeriesRosterMatcher.BookKey.From(expected.Position, expected.Title));

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
