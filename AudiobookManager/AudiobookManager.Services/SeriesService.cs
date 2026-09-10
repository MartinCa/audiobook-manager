using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Services.Similarity;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

public class SeriesService : ISeriesService
{
    /// <summary>
    /// Minimum normalized title similarity for an owned book to be considered the same book
    /// as a roster entry when positions don't settle it.
    /// </summary>
    private const double TitleMatchThreshold = 0.85;

    /// <summary>
    /// Sanity floor applied when positions match: sources renumber and split series
    /// differently than a hand-maintained library does (a novella at source position 2.5 vs a
    /// manually typed "2.5" on an unrelated book), so a matching position must not on its own
    /// declare an obviously different title to be the same book - which would silently hide a
    /// genuinely missing entry. Edit-distance similarity alone is a poor floor here (two
    /// unrelated titles routinely score around 0.3), so shared whole words count too: a
    /// subtitled or abridged edition keeps the words even when the string lengths diverge.
    /// </summary>
    private const double PositionMatchTitleFloor = 0.5;

    /// <summary>
    /// Minimum normalized title similarity for a library book to surface as a candidate for a
    /// missing expected book. Deliberately looser than <see cref="TitleMatchThreshold"/>: these
    /// are advisory candidates a human confirms, and a subtitled or lightly renamed edition must
    /// still surface - a false positive costs a glance, a false negative hides the very book the
    /// user is looking for.
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
    private readonly IAudiobookService _audiobookService;
    private readonly ISeriesReconciliationCache _reconciliationCache;
    private readonly IEnumerable<IScraper> _scrapers;
    private readonly ILogger<SeriesService> _logger;

    public SeriesService(
        IAudiobookRepository audiobookRepository,
        ISeriesRepository seriesRepository,
        IAudiobookService audiobookService,
        ISeriesReconciliationCache reconciliationCache,
        IEnumerable<IScraper> scrapers,
        ILogger<SeriesService> logger)
    {
        _audiobookRepository = audiobookRepository;
        _seriesRepository = seriesRepository;
        _audiobookService = audiobookService;
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
        int page, int pageSize, string? search, bool? matched)
    {
        var (names, totalCount) = await _audiobookRepository.GetSeriesValuesPageAsync(
            search, matched, skip: (int)((long)page * pageSize), take: pageSize);

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
        int ignoredSkip, int ignoredTake)
    {
        // The per-request reads are bounded: one catalog metadata row, one SQL page of owned
        // books, and the cached reconciliation. The reconciliation itself - which classifies the
        // whole roster against the series' owned keys via the fuzzy matcher - is computed once
        // per series per change, never per page request (see GetOrComputeReconciliationAsync).
        var catalogRow = await _seriesRepository.GetByNameAsync(seriesName);
        var ownedPage = await _audiobookRepository.GetSeriesOwnedBooksPageAsync(seriesName, ownedSkip, ownedTake);

        if (ownedPage.Total == 0 && catalogRow is null)
        {
            return null;
        }

        var reconciliation = await GetOrComputeReconciliationAsync(seriesName);

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
        };
    }

    /// <summary>
    /// The series' reconciliation, computed once per series per change and cached. The
    /// computation classifies the full roster against the series' owned (position, title) keys -
    /// the fuzzy ownership semantics demand the whole key set, which SQL cannot express - so it
    /// is the one step that touches per-series data beyond the requested page, and it is bounded:
    /// the roster is the metadata source's stored series page and both inputs sit under the
    /// <see cref="MaxReconciliationRosterEntries"/>/<see cref="MaxReconciliationOwnedKeys"/>
    /// caps. The single-flight gate, the version check and the capacity-bounded eviction all live
    /// inside <see cref="ISeriesReconciliationCache"/>, so a page-flip stampede shares one
    /// computation and the cache cannot grow with the library.
    /// </summary>
    private Task<SeriesReconciliation> GetOrComputeReconciliationAsync(string seriesName) =>
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

        var ownedIndex = new OwnedBookIndex(ownedKeys.Select(k => BookKey.From(k.SeriesPart, k.BookName)));
        var missing = visible
            .Where(e => !e.IsIgnored && !IsOwned(e, ownedIndex))
            .Select(ToExpectedInfo)
            .OrderBy(e => PositionSortKey(e.Position))
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

        var ignored = visible
            .Where(e => e.IsIgnored)
            .Select(ToExpectedInfo)
            .OrderBy(e => PositionSortKey(e.Position))
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

        var authors = await _audiobookRepository.GetAuthorNamesBySeriesAsync(seriesName);

        return new SeriesReconciliation(
            missing,
            ignored,
            ExpectedBookCount: visible.Count(e => !e.IsIgnored),
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
        var reconciliation = await GetOrComputeReconciliationAsync(seriesName);
        return BuildReconciledOverview(seriesName, saved, reconciliation);
    }

    public async Task<SeriesOverview> SetIncludeOmnibusEditionsAsync(string seriesName, bool includeOmnibusEditions)
    {
        // The full roster (compilations included) is always stored, so this is a pure display
        // setting - no re-fetch from the source is needed to apply it. It does change which
        // roster entries are visible, so the cached reconciliation must be dropped and rebuilt.
        var saved = await _seriesRepository.SetIncludeOmnibusEditionsAsync(seriesName, includeOmnibusEditions);
        _reconciliationCache.Invalidate(seriesName);

        var reconciliation = await GetOrComputeReconciliationAsync(seriesName);
        return BuildReconciledOverview(seriesName, saved, reconciliation);
    }

    /// <summary>
    /// Matches the series to a source and replaces its stored roster, returning the catalog
    /// row. Does no read-side projection work.
    /// </summary>
    private async Task<Series> MatchSeriesCoreAsync(
        string seriesName,
        string sourceName,
        string sourceSeriesId,
        double? confidence,
        bool includeOmnibusEditions,
        Series? existingRow = null)
    {
        var scraper = SeriesCapableScrapers.FirstOrDefault(s => s.IsSource(sourceName))
            ?? throw new ArgumentException($"No series-capable scraper for source {sourceName}");

        var roster = await scraper.GetSeriesBooks(sourceSeriesId)
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
            .Select(p => BookKey.From(p.Position, p.Title))
            .ToList();

        // The full roster is always stored, compilations included - IncludeOmnibusEditions only
        // controls what SeriesService treats as visible when reading it back, so toggling it
        // later doesn't require re-fetching from the source.
        var newExpected = roster.Books.Select(b =>
        {
            var key = BookKey.From(b.Position, b.Title);
            return new SeriesExpectedBook
            {
                Position = b.Position,
                Title = b.Title,
                Year = b.Year,
                SourceUrl = b.SourceUrl,
                IsCompilation = b.IsCompilation,
                // Re-matching or refreshing replaces the roster wholesale, so carry the user's
                // ignore decisions across for entries that are recognisably the same book.
                IsIgnored = previouslyIgnored.Any(p => IsSameBook(p, key)),
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

    public Task<(int Processed, int Succeeded, int Failed, string? StopReason)> RefreshSeriesAsync(
        string seriesName,
        Func<int, int, int, int, Task> progressAction) =>
        RefreshManyAsync(new List<string> { seriesName }, progressAction);

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
        var books = await _audiobookRepository.GetSeriesCandidateDataAsync(expected.Title, CandidatePrefilterLimit);

        // A book already in the target series is deliberately NOT excluded: one with a wrong part
        // or a slightly different title is exactly what leaves a roster entry reported as missing,
        // and the candidate carries its current series/part so the UI can show it.
        return books
            .Select(book => (Book: book, TitleSimilarity: TitleSimilarity(book.BookName, expected.Title)))
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

            await MatchSeriesCoreAsync(
                name, row.MatchedSourceName, row.MatchedSourceId, row.MatchConfidence, row.IncludeOmnibusEditions, row);
            return true;
        });
    }

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
        var ownedIndex = new OwnedBookIndex(ownedBooks.Select(b => BookKey.From(b.SeriesPart, b.BookName)));

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
    /// A book reduced to what the owned/expected comparison needs, with its title normalized
    /// once up front - the matching loop is O(expected x owned), so re-normalizing per
    /// comparison would repeat the same work for every candidate.
    /// </summary>
    private readonly record struct BookKey(string? Position, string NormalizedTitle)
    {
        public static BookKey From(string? position, string? title) => new(position, NameNormalizer.Normalize(title));
    }

    /// <summary>
    /// The owned books of a series, with an index over their positions. The matching loop is
    /// O(expected x owned) and every miss pays for a Levenshtein matrix, so the common case -
    /// source and library agreeing on the position - is settled by a dictionary hit before any
    /// scanning starts. The scan itself is still over *every* owned book: a position match is
    /// only one of the two ways <see cref="IsSameBook"/> can succeed, and a roster entry with no
    /// position (or a position nobody else uses) must still be compared on title against books
    /// that do have one. Partitioning the fallback by position instead of just short-circuiting
    /// ahead of it silently reported owned books as missing.
    /// </summary>
    private sealed class OwnedBookIndex
    {
        private readonly List<BookKey> _keys;
        private readonly ILookup<string, BookKey> _byPosition;

        public OwnedBookIndex(IEnumerable<BookKey> ownedKeys)
        {
            _keys = ownedKeys.ToList();
            _byPosition = _keys
                .Where(k => !string.IsNullOrWhiteSpace(k.Position))
                .ToLookup(k => NormalizePosition(k.Position!), StringComparer.OrdinalIgnoreCase);
        }

        public bool Contains(BookKey expected)
        {
            // Fast path only - never a substitute for the scan below.
            if (!string.IsNullOrWhiteSpace(expected.Position))
            {
                foreach (var owned in _byPosition[NormalizePosition(expected.Position!)])
                {
                    if (IsSameBook(expected, owned))
                    {
                        return true;
                    }
                }
            }

            return _keys.Any(owned => IsSameBook(expected, owned));
        }

        /// <summary>
        /// Positions are free text that may or may not parse as a number ("2", "2.0", "2.5",
        /// "Book 2"). Numeric ones are keyed by their invariant round-trip so the index groups
        /// them exactly as <see cref="PositionsEqual"/> compares them; the rest key on their
        /// trimmed text, which is the fallback that method uses too.
        /// </summary>
        private static string NormalizePosition(string position) =>
            double.TryParse(position, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var numeric)
                ? numeric.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                : position.Trim();
    }

    /// <summary>
    /// Whether any owned book corresponds to this roster entry. Owned book names rarely match
    /// a source title byte-for-byte, so an exact position match counts, and otherwise titles
    /// are compared fuzzily.
    /// </summary>
    private static bool IsOwned(SeriesExpectedBook expected, OwnedBookIndex ownedBooks) =>
        ownedBooks.Contains(BookKey.From(expected.Position, expected.Title));

    private static bool IsSameBook(BookKey expected, BookKey owned)
    {
        var positionsMatch =
            !string.IsNullOrWhiteSpace(expected.Position) &&
            !string.IsNullOrWhiteSpace(owned.Position) &&
            PositionsEqual(expected.Position, owned.Position);

        // With no title to compare on either side, the position is all there is to go on.
        if (positionsMatch && (expected.NormalizedTitle.Length == 0 || owned.NormalizedTitle.Length == 0))
        {
            return true;
        }

        // A matching position only needs the titles to be non-contradictory; a strong title
        // match stands on its own even when the positions disagree (users mistype them).
        if (positionsMatch)
        {
            return TitlesNotContradictory(expected.NormalizedTitle, owned.NormalizedTitle);
        }

        return NormalizedSimilarity(expected.NormalizedTitle, owned.NormalizedTitle, TitleMatchThreshold) >= TitleMatchThreshold;
    }

    private static bool TitlesNotContradictory(string normA, string normB)
    {
        if (NormalizedSimilarity(normA, normB, PositionMatchTitleFloor) >= PositionMatchTitleFloor)
        {
            return true;
        }

        var tokensA = normA.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var tokensB = normB.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return false;
        }

        var shared = tokensA.Count(t => tokensB.Contains(t));
        return shared / (double)Math.Min(tokensA.Count, tokensB.Count) >= PositionMatchTitleFloor;
    }

    private static bool PositionsEqual(string a, string b)
    {
        if (double.TryParse(a, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var numA) &&
            double.TryParse(b, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var numB))
        {
            return Math.Abs(numA - numB) < 0.0001;
        }

        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 0..1 similarity of two free-text values, using the shared comparison-only normalizer
    /// and edit distance scaled by the longer string's length.
    /// </summary>
    public static double TitleSimilarity(string? a, string? b) =>
        NormalizedSimilarity(NameNormalizer.Normalize(a), NameNormalizer.Normalize(b));

    /// <summary>
    /// Similarity of two already-normalized strings. When the caller only cares whether the
    /// score reaches <paramref name="threshold"/>, the length difference (a lower bound on
    /// the edit distance) can rule the pair out before the O(n*m) distance matrix is built.
    /// The threshold stays here rather than inside LevenshteinDistance, which is
    /// general-purpose.
    /// </summary>
    private static double NormalizedSimilarity(string normA, string normB, double threshold = 0)
    {
        if (normA.Length == 0 || normB.Length == 0)
        {
            return 0;
        }

        if (normA == normB)
        {
            return 1;
        }

        var longest = Math.Max(normA.Length, normB.Length);

        if (threshold > 0 && Math.Abs(normA.Length - normB.Length) / (double)longest > 1 - threshold)
        {
            return 0;
        }

        var distance = LevenshteinDistance.Compute(normA, normB);

        return Math.Max(0, 1.0 - (double)distance / longest);
    }

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

    private static (double Numeric, string Text) PositionSortKey(string? position)
    {
        if (string.IsNullOrWhiteSpace(position))
        {
            return (double.MaxValue, string.Empty);
        }

        if (double.TryParse(position, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var numeric))
        {
            return (numeric, string.Empty);
        }

        return (double.MaxValue - 1, position);
    }
}
