using AudiobookManager.Database.Repositories;
using AudiobookManager.Database.Search;
using AudiobookManager.Domain;
using AudiobookManager.Services.Similarity;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Services;

public class SimilarValueService : ISimilarValueService
{
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IPersonRepository _personRepository;
    private readonly IAudiobookService _audiobookService;
    private readonly IAudiobookSaveGate _saveGate;
    private readonly ISimilarValueDetectionCache _detectionCache;
    private readonly AudiobookManagerSettings _settings;
    private readonly ILogger<SimilarValueService> _logger;

    private const string AuthorGroupsKind = "authors";
    private const string SeriesGroupsKind = "series";

    /// <summary>
    /// The candidate-prefilter cap for the entry-status classification. The fuzzy scoring runs
    /// over this bounded set - never the whole distinct-value list - selected by an
    /// accent-insensitive LIKE, the same containment filter the search dialogs use.
    /// </summary>
    private const int EntryStatusCandidatePrefilterLimit = 20;

    /// <summary>Hard cap on the similar matches one entry-status response can carry.</summary>
    private const int EntryStatusMaxMatches = 3;

    public SimilarValueService(
        IAudiobookRepository audiobookRepository,
        IPersonRepository personRepository,
        IAudiobookService audiobookService,
        IAudiobookSaveGate saveGate,
        ISimilarValueDetectionCache detectionCache,
        IOptions<AudiobookManagerSettings> settings,
        ILogger<SimilarValueService> logger)
    {
        _audiobookRepository = audiobookRepository;
        _personRepository = personRepository;
        _audiobookService = audiobookService;
        _saveGate = saveGate;
        _detectionCache = detectionCache;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<(List<SimilarValueGroup> Items, int Total)> DetectSimilarAuthorsAsync(int skip, int take)
    {
        // Detection only needs the distinct author names - not the per-book references the old
        // implementation loaded for every author on every request - and the clustered groups are
        // cached, so paging through the results does not re-run the clustering per request.
        var groups = await GetOrComputeGroupsAsync(AuthorGroupsKind, _personRepository.GetAuthorNamesAsync);

        var (items, total) = Page(groups, skip, take);

        // Book counts are fetched only for the candidates the returned page shows, not for every
        // value in the library.
        await StampBookCountsAsync(items, _personRepository.GetAuthorBookCountsAsync);

        return (items, total);
    }

    public async Task<(List<SimilarValueGroup> Items, int Total)> DetectSimilarSeriesAsync(int skip, int take)
    {
        var groups = await GetOrComputeGroupsAsync(SeriesGroupsKind, _audiobookRepository.GetSeriesNamesAsync);

        var (items, total) = Page(groups, skip, take);

        await StampBookCountsAsync(items, _audiobookRepository.GetSeriesBookCountsAsync);

        return (items, total);
    }

    /// <summary>
    /// Classifies one typed entry: an accent-/case-insensitive existing value is "exact";
    /// otherwise a normalizer/edit-distance/substring close value is "similar"; otherwise "new".
    /// The reads are bounded (exact lookup + capped LIKE prefilter), so this never scans the
    /// library's whole set of distinct values, and the response is capped at <paramref name="limit"/>.
    /// </summary>
    public async Task<EntryValueStatus> GetEntryStatusAsync(EntryValueKind kind, string value, int limit)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new EntryValueStatus(trimmed, EntryValueStatusKind.New, null, new List<EntryValueMatch>());
        }

        var resultLimit = Math.Max(1, Math.Min(limit, EntryStatusMaxMatches));

        if (kind == EntryValueKind.Author)
        {
            return await GetPersonEntryStatusAsync(
                trimmed,
                resultLimit,
                _personRepository.FindAuthorByFoldedNameAsync,
                _personRepository.SearchAuthorNamesAsync);
        }

        if (kind == EntryValueKind.Narrator)
        {
            // Narrators are Person rows too, so the classification is the author one with the
            // narrator-owned queries - a person that only authors books is not an existing
            // narrator, exactly as a narrator-only person is not an existing author.
            return await GetPersonEntryStatusAsync(
                trimmed,
                resultLimit,
                _personRepository.FindNarratorByFoldedNameAsync,
                _personRepository.SearchNarratorNamesAsync);
        }

        var exactSeries = await _audiobookRepository.FindSeriesValueByFoldedNameAsync(trimmed);
        if (exactSeries is not null)
        {
            return new EntryValueStatus(
                trimmed,
                EntryValueStatusKind.Exact,
                new EntryValueMatch(null, exactSeries),
                new List<EntryValueMatch>());
        }

        var seriesCandidates = await _audiobookRepository.SearchSeriesValuesAsync(
            trimmed, EntryStatusCandidatePrefilterLimit);
        return BuildSimilarOrNew(trimmed,
            seriesCandidates.Select(s => new EntryValueMatch(null, s)), resultLimit);
    }

    private async Task<EntryValueStatus> GetPersonEntryStatusAsync(
        string trimmed,
        int resultLimit,
        Func<string, Task<AuthorSummaryRow?>> findExact,
        Func<string, int, Task<List<AuthorSummaryRow>>> searchCandidates)
    {
        var exact = await findExact(trimmed);
        if (exact is not null)
        {
            return new EntryValueStatus(
                trimmed,
                EntryValueStatusKind.Exact,
                new EntryValueMatch(exact.Id, exact.Name),
                new List<EntryValueMatch>());
        }

        var candidates = await searchCandidates(trimmed, EntryStatusCandidatePrefilterLimit);
        return BuildSimilarOrNew(trimmed,
            candidates.Select(c => new EntryValueMatch(c.Id, c.Name)), resultLimit);
    }

    private EntryValueStatus BuildSimilarOrNew(
        string value, IEnumerable<EntryValueMatch> candidates, int limit)
    {
        var matches = ScoreSimilarMatches(value, candidates).Take(limit).ToList();
        return matches.Count > 0
            ? new EntryValueStatus(value, EntryValueStatusKind.Similar, null, matches)
            : new EntryValueStatus(value, EntryValueStatusKind.New, null, new List<EntryValueMatch>());
    }

    /// <summary>
    /// The candidate values that are genuinely similar to the typed one, ranked closest first
    /// (edit distance, then name). The exact lookup has already run, so a normalized-equal or
    /// folded-equal candidate here means "same value, different raw spelling" (e.g. "Jane
    /// Authorr" vs "Jane Author" is a near match; "René" vs "Rene" was already exact).
    /// </summary>
    private List<EntryValueMatch> ScoreSimilarMatches(string value, IEnumerable<EntryValueMatch> candidates)
    {
        var normInput = NameNormalizer.Normalize(value);
        var foldedInput = Folded(value);
        var scored = new List<(EntryValueMatch Match, int Distance)>();

        foreach (var candidate in candidates)
        {
            if (candidate.Name == value || string.IsNullOrWhiteSpace(candidate.Name))
            {
                continue;
            }

            var normCandidate = NameNormalizer.Normalize(candidate.Name);
            if (normCandidate == normInput
                || normCandidate.Contains(normInput)
                || normInput.Contains(normCandidate))
            {
                scored.Add((candidate, 0));
                continue;
            }

            var foldedCandidate = Folded(candidate.Name);
            if (foldedCandidate == foldedInput
                || foldedCandidate.Contains(foldedInput)
                || foldedInput.Contains(foldedCandidate))
            {
                scored.Add((candidate, 0));
                continue;
            }

            var distance = LevenshteinDistance.Compute(normInput, normCandidate);
            if (distance <= MaxSimilarDistance(normInput.Length, normCandidate.Length))
            {
                scored.Add((candidate, distance));
            }
        }

        return scored
            .OrderBy(s => s.Distance)
            .ThenBy(s => s.Match.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Match)
            .ToList();
    }

    /// <summary>
    /// The length-scaled edit-distance bar, mirroring <see cref="SimilarityGrouper"/>'s blocking:
    /// short values (at or under <see cref="AudiobookManagerSettings.SimilarityShortLength"/>)
    /// never fuzzy-match on edit distance alone (a two-letter author would match almost anything),
    /// medium ones allow one edit, longer ones two. Substring/normalized equality is scored
    /// separately above, so this only gates genuine near-spelling fuzzy matches.
    /// </summary>
    private int MaxSimilarDistance(int inputLength, int candidateLength)
    {
        var length = Math.Min(inputLength, candidateLength);
        if (length <= _settings.SimilarityShortLength)
        {
            return 0;
        }

        return length <= _settings.SimilarityMediumLength
            ? _settings.SimilarityMaxDistanceMedium
            : _settings.SimilarityMaxDistanceLong;
    }

    private static string Folded(string value) =>
        (AccentFolding.FoldPlain(value) ?? string.Empty).ToLowerInvariant();

    /// <summary>
    /// The clustered groups for one value kind, computing them from the distinct values when the
    /// cache is empty or stale. The cache stores name clusters only (book counts are read fresh
    /// per page), so a returned page is never older than a few minutes in its grouping and never
    /// stale in its counts.
    ///
    /// The version is captured before the compute starts and handed to
    /// <see cref="ISimilarValueDetectionCache.Set"/>, so an alignment invalidation landing while
    /// the distinct values are being read cannot let this method republish pre-alignment groups
    /// for the TTL - the publish is dropped instead. Two callers racing a miss may both compute;
    /// the result is identical and only the current-version publish wins.
    ///
    /// The stored graph is treated as read-only: <see cref="Page"/> returns copies of its groups
    /// and candidates, so the per-page book-count stamping in <see cref="StampBookCountsAsync"/>
    /// never writes through to the objects the cache holds.
    /// </summary>
    private async Task<List<SimilarValueGroup>> GetOrComputeGroupsAsync(
        string kind, Func<Task<List<string>>> loadDistinctValues)
    {
        var versionAtStart = _detectionCache.GetVersion();
        if (_detectionCache.Get(kind) is { } cached)
        {
            return cached;
        }

        var values = await loadDistinctValues();
        var clusters = SimilarityGrouper.GroupSimilarValues(values, _settings);

        var groups = clusters
            .Select(cluster => new SimilarValueGroup
            {
                Candidates = cluster.Select(value => new SimilarValueCandidate { Value = value }).ToList(),
            })
            // The deterministic key a group's page order sorts by; see FirstCandidate.
            .OrderBy(g => FirstCandidate(g), StringComparer.Ordinal)
            .ToList();

        // The caller still gets this request's result either way (it is a consistent snapshot of
        // what the read saw); a dropped publish just means the cache stays empty and the next
        // request recomputes against the post-alignment data.
        _detectionCache.Set(kind, groups, versionAtStart);
        return groups;
    }

    private static async Task StampBookCountsAsync(
        List<SimilarValueGroup> items,
        Func<IReadOnlyCollection<string>, Task<Dictionary<string, int>>> fetchCounts)
    {
        if (items.Count == 0)
        {
            return;
        }

        var values = items.SelectMany(g => g.Candidates).Select(c => c.Value).ToList();
        var counts = await fetchCounts(values);

        foreach (var candidate in items.SelectMany(g => g.Candidates))
        {
            candidate.BookCount = counts.TryGetValue(candidate.Value, out var count) ? count : 0;
        }
    }

    /// <summary>
    /// The deterministic key a group's page order sorts by. Groups come from the cache or from a
    /// deterministic clustering of the current distinct values, and are sorted here so the same
    /// request always returns the same slice - otherwise paging could repeat or drop a group.
    /// </summary>
    private static string FirstCandidate(SimilarValueGroup group) => group.Candidates.FirstOrDefault()?.Value ?? string.Empty;

    private static (List<SimilarValueGroup> Items, int Total) Page(List<SimilarValueGroup> groups, int skip, int take) =>
        (groups.Skip(skip).Take(take).Select(CloneGroup).ToList(), groups.Count);

    /// <summary>
    /// A returned page is a copy, not a window, over the cached detection snapshot: book counts
    /// are stamped onto the returned candidates (<see cref="StampBookCountsAsync"/>), and writing
    /// through to the cache's objects would make the snapshot mutable - two concurrent reads of
    /// the same group would race a plain field write against the other request's serialization.
    /// </summary>
    private static SimilarValueGroup CloneGroup(SimilarValueGroup group) =>
        new()
        {
            Candidates = group.Candidates
                .Select(c => new SimilarValueCandidate { Value = c.Value, BookCount = c.BookCount })
                .ToList(),
        };

    public async Task<(int Processed, int Succeeded, int Failed)> AlignAuthorsAsync(
        List<string> sourceNames,
        string targetName,
        Func<int, int, int, int, Task> progressAction)
    {
        // sourceNames is the full candidate group, which includes targetName itself. Excluding it
        // before querying avoids re-processing books that already only have the target author name.
        var namesToAlign = sourceNames.Where(n => n != targetName).ToList();
        if (namesToAlign.Count == 0)
        {
            return (0, 0, 0);
        }

        var sourceSet = new HashSet<string>(namesToAlign, StringComparer.Ordinal);
        var books = await _audiobookRepository.GetBooksByAuthorNamesAsync(namesToAlign);

        var result = await BulkOperationRunner.RunAsync(
            books,
            async dbBook =>
            {
                // Alignment rewrites the book's tags and can relocate its file, so it takes the
                // same per-audiobook gate an interactive save does. A book someone is saving
                // right now fails just its own item - BulkOperationRunner counts it and the
                // batch carries on.
                using var lease = _saveGate.Acquire(dbBook.Id);

                var domain = AudiobookService.FromDb(dbBook);
                domain.Id = dbBook.Id;

                // Track whether the target name is already present in newAuthors via a bool
                // that is kept in sync on every insertion (including when the target is already
                // a literal author on the book, not just when it's added to replace a source
                // name) so a source name encountered later never re-adds it as a duplicate.
                var newAuthors = new List<Person>();
                var targetPresent = false;
                foreach (var author in domain.Authors)
                {
                    if (sourceSet.Contains(author.Name))
                    {
                        if (!targetPresent)
                        {
                            newAuthors.Add(new Person(targetName));
                            targetPresent = true;
                        }
                    }
                    else if (newAuthors.All(a => a.Name != author.Name))
                    {
                        newAuthors.Add(author);
                        if (author.Name == targetName)
                        {
                            targetPresent = true;
                        }
                    }
                }
                domain.Authors = newAuthors;

                await _audiobookService.UpdateAudiobook(dbBook.Id, domain);
            },
            _logger,
            dbBook => $"Failed to align author for audiobook {dbBook.Id}",
            progressAction);

        // The merge folds groups together; the cached detection must not keep serving the
        // pre-merge grouping until its TTL runs out.
        _detectionCache.Invalidate();

        return result;
    }

    public async Task<(int Processed, int Succeeded, int Failed)> AlignSeriesAsync(
        List<string> sourceValues,
        string targetValue,
        Func<int, int, int, int, Task> progressAction)
    {
        // sourceValues is the full candidate group, which includes targetValue itself. Excluding it
        // before querying avoids re-processing books whose series already matches the target.
        var valuesToAlign = sourceValues.Where(v => v != targetValue).ToList();
        if (valuesToAlign.Count == 0)
        {
            return (0, 0, 0);
        }

        var books = await _audiobookRepository.GetBooksBySeriesValuesAsync(valuesToAlign);

        var result = await BulkOperationRunner.RunAsync(
            books,
            async dbBook =>
            {
                // See AlignAuthorsAsync: the same per-audiobook gate, for the same reason.
                using var lease = _saveGate.Acquire(dbBook.Id);

                var domain = AudiobookService.FromDb(dbBook);
                domain.Id = dbBook.Id;
                domain.Series = targetValue;

                await _audiobookService.UpdateAudiobook(dbBook.Id, domain);
            },
            _logger,
            dbBook => $"Failed to align series for audiobook {dbBook.Id}",
            progressAction);

        _detectionCache.Invalidate();

        return result;
    }
}
