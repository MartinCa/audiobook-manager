using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;
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

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly IEnumerable<IScraper> _scrapers;
    private readonly ILogger<SeriesService> _logger;

    public SeriesService(
        IAudiobookRepository audiobookRepository,
        ISeriesRepository seriesRepository,
        IEnumerable<IScraper> scrapers,
        ILogger<SeriesService> logger)
    {
        _audiobookRepository = audiobookRepository;
        _seriesRepository = seriesRepository;
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

    public async Task<SeriesDetail?> GetSeriesDetailAsync(string seriesName)
    {
        var books = await _audiobookRepository.GetBooksBySeriesAsync(seriesName, null);
        var catalogRow = await _seriesRepository.GetByNameWithExpectedBooksAsync(seriesName);

        if (books.Count == 0 && catalogRow is null)
        {
            return null;
        }

        var overview = BuildOverview(seriesName, ToGroupingBooks(books), catalogRow);

        var expected = catalogRow?.ExpectedBooks ?? new List<SeriesExpectedBook>();
        var ownedKeys = books.Select(b => BookKey.From(b.SeriesPart, b.BookName)).ToList();
        var missing = expected
            .Where(e => !e.IsIgnored && !IsOwned(e, ownedKeys))
            .Select(ToExpectedInfo)
            .OrderBy(e => PositionSortKey(e.Position))
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ignored = expected
            .Where(e => e.IsIgnored)
            .Select(ToExpectedInfo)
            .OrderBy(e => PositionSortKey(e.Position))
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SeriesDetail
        {
            Overview = overview,
            OwnedBooks = books
                .Select(b => new SeriesOwnedBook
                {
                    Id = b.Id,
                    BookName = b.BookName,
                    SeriesPart = b.SeriesPart,
                    Year = b.Year,
                    Authors = b.Authors.Select(a => a.Name).ToList(),
                    Narrators = b.Narrators.Select(n => n.Name).ToList(),
                    DurationInSeconds = b.DurationInSeconds,
                })
                .OrderBy(b => PositionSortKey(b.SeriesPart))
                .ThenBy(b => b.BookName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MissingBooks = missing,
            IgnoredBooks = ignored,
        };
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
        (await _audiobookRepository.GetBooksBySeriesAsync(seriesName, null))
            .SelectMany(b => b.Authors.Select(a => a.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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

        // Only the single-series endpoint needs the full detail projection; bulk callers use
        // MatchSeriesCoreAsync directly and would throw the result away.
        var detail = await GetSeriesDetailAsync(seriesName);
        return detail?.Overview ?? BuildOverview(seriesName, new List<SeriesGroupingBook>(), saved);
    }

    public async Task<SeriesOverview> SetIncludeOmnibusEditionsAsync(string seriesName, bool includeOmnibusEditions)
    {
        var existing = await _seriesRepository.GetByNameWithExpectedBooksAsync(seriesName);

        if (existing is not null && !string.IsNullOrEmpty(existing.MatchedSourceName) && !string.IsNullOrEmpty(existing.MatchedSourceId))
        {
            await MatchSeriesCoreAsync(seriesName, existing.MatchedSourceName, existing.MatchedSourceId, existing.MatchConfidence, includeOmnibusEditions);
        }
        else
        {
            await _seriesRepository.UpsertSeriesAsync(new Series
            {
                Name = seriesName,
                IncludeOmnibusEditions = includeOmnibusEditions,
            });
        }

        var detail = await GetSeriesDetailAsync(seriesName);
        return detail?.Overview ?? BuildOverview(seriesName, new List<SeriesGroupingBook>(), existing);
    }

    /// <summary>
    /// Matches the series to a source and replaces its stored roster, returning the catalog
    /// row. Does no read-side projection work.
    /// </summary>
    private async Task<Series> MatchSeriesCoreAsync(string seriesName, string sourceName, string sourceSeriesId, double? confidence, bool includeOmnibusEditions)
    {
        var scraper = SeriesCapableScrapers.FirstOrDefault(s => s.IsSource(sourceName))
            ?? throw new ArgumentException($"No series-capable scraper for source {sourceName}");

        var roster = await scraper.GetSeriesBooks(sourceSeriesId)
            ?? throw new Exception($"Source {sourceName} returned no series for id {sourceSeriesId}");

        var existing = await _seriesRepository.GetByNameWithExpectedBooksAsync(seriesName);

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

        var newExpected = roster.Books
            .Where(b => includeOmnibusEditions || !b.IsCompilation)
            .Select(b =>
            {
                var key = BookKey.From(b.Position, b.Title);
                return new SeriesExpectedBook
                {
                    Position = b.Position,
                    Title = b.Title,
                    Year = b.Year,
                    SourceUrl = b.SourceUrl,
                    // Re-matching or refreshing replaces the roster wholesale, so carry the user's
                    // ignore decisions across for entries that are recognisably the same book.
                    IsIgnored = previouslyIgnored.Any(p => IsSameBook(p, key)),
                };
            }).ToList();

        await _seriesRepository.ReplaceExpectedBooksAsync(saved.Id, newExpected);

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

    public Task IgnoreExpectedBookAsync(string seriesName, string? position, string? title, bool ignored) =>
        _seriesRepository.SetExpectedBookIgnoredAsync(seriesName, position, title, ignored);

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

            await MatchSeriesCoreAsync(name, row.MatchedSourceName, row.MatchedSourceId, row.MatchConfidence, row.IncludeOmnibusEditions);
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

    private static List<SeriesGroupingBook> ToGroupingBooks(IEnumerable<DbAudiobook> books) =>
        books
            .Select(b => new SeriesGroupingBook(
                b.Series ?? string.Empty,
                b.SeriesPart,
                b.BookName,
                b.Authors.Select(a => a.Name).ToList()))
            .ToList();

    private static SeriesOverview BuildOverview(string seriesName, List<SeriesGroupingBook> ownedBooks, Series? catalogRow)
    {
        var expected = catalogRow?.ExpectedBooks ?? new List<SeriesExpectedBook>();
        var active = expected.Where(e => !e.IsIgnored).ToList();
        var ownedKeys = ownedBooks.Select(b => BookKey.From(b.SeriesPart, b.BookName)).ToList();

        return new SeriesOverview
        {
            Id = catalogRow?.Id,
            Name = seriesName,
            Authors = ownedBooks
                .SelectMany(b => b.Authors)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            OwnedBookCount = ownedBooks.Count,
            IsMatched = catalogRow is not null
                && !string.IsNullOrEmpty(catalogRow.MatchedSourceName)
                && !string.IsNullOrEmpty(catalogRow.MatchedSourceId),
            MatchedSourceName = catalogRow?.MatchedSourceName,
            MatchedSourceId = catalogRow?.MatchedSourceId,
            MatchedSourceUrl = catalogRow?.MatchedSourceUrl,
            MatchConfidence = catalogRow?.MatchConfidence,
            LastRefreshedAt = catalogRow?.LastRefreshedAt,
            ExpectedBookCount = active.Count,
            IgnoredBookCount = expected.Count - active.Count,
            MissingBookCount = active.Count(e => !IsOwned(e, ownedKeys)),
            IncludeOmnibusEditions = catalogRow?.IncludeOmnibusEditions ?? false,
        };
    }

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
    /// Whether any owned book corresponds to this roster entry. Owned book names rarely match
    /// a source title byte-for-byte, so an exact position match counts, and otherwise titles
    /// are compared fuzzily.
    /// </summary>
    private static bool IsOwned(SeriesExpectedBook expected, IReadOnlyCollection<BookKey> ownedKeys)
    {
        var expectedKey = BookKey.From(expected.Position, expected.Title);
        return ownedKeys.Any(owned => IsSameBook(expectedKey, owned));
    }

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
