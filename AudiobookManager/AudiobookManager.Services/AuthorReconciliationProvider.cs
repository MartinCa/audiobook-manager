using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

/// <summary>
/// See <see cref="IAuthorReconciliationProvider"/>. Mirrors
/// <see cref="SeriesReconciliationProvider"/>'s matching and classification against the unified
/// <see cref="ExpectedBook"/> rows, minus the part-mismatch section the author view has no use for
/// (fixing a book's part is the series view's job once the series is matched). Unlike the series
/// reconciliation there is no cache: an author's roster is bounded the same way a series roster
/// is, so this recomputes per request - see that interface for the tradeoff.
///
/// The author's roster spans the whole bibliography, series books included (the same source book
/// discovered by an author refresh and by a series refresh is one row, linked to both scopes), so
/// the matching must be series-aware:
/// <list type="bullet">
/// <item>a standalone entry (no source or local series) matches an owned book by title alone - the
/// same <see cref="SeriesRosterMatcher"/> title semantics, over every owned book of the author
/// (a source that erases a series placement must not report the owned book as missing);</item>
/// <item>an entry linked to a local series matches an owned book of that same local series value by
/// position/title - the full <see cref="SeriesRosterMatcher"/> semantics scoped to the series;</item>
/// <item>an entry whose source series is not yet matched to a local series has no local series name
/// to scope against, so it is matched best-effort by position and/or title over everything the
/// author owns - documented as potentially incomplete until the series is matched (two different
/// series can carry same-titled books, and the source position can disagree with a hand-typed
/// part).</item>
/// </list>
/// A book already owned must never appear missing or upcoming.
/// </summary>
public class AuthorReconciliationProvider : IAuthorReconciliationProvider
{
    /// <summary>Same rationale as <see cref="SeriesReconciliationProvider.MaxReconciliationRosterEntries"/>, scoped to one author's whole bibliography.</summary>
    public const int MaxReconciliationRosterEntries = 5_000;

    /// <summary>Same rationale as <see cref="SeriesReconciliationProvider.MaxReconciliationOwnedKeys"/>, scoped to one author's owned books.</summary>
    public const int MaxReconciliationOwnedKeys = 20_000;

    /// <summary>
    /// The largest number of distinct source-series groups the author's missing-series computation
    /// will classify in memory - enforced BEFORE the groups are materialized (the roster fetch is
    /// already bounded to <see cref="MaxReconciliationRosterEntries"/> rows, and the group count is
    /// an integer pass over that bounded list, so a pathological value refuses with a clear error
    /// rather than an unbounded per-author list). A per-author detail computation, never a global
    /// list.
    /// </summary>
    public const int MaxAuthorSeriesGroups = 500;

    /// <summary>
    /// The largest active author-linked expected-book reference set the bulk authors-list filter
    /// will read in one pass - the whole-library input of
    /// <see cref="GetBulkMissingOrUpcomingAuthorIdsAsync"/>, capped so the filter never
    /// materializes the unified roster table whole. A library past it is refused (see that
    /// method) rather than silently truncated into a wrong filter result.
    /// </summary>
    public const int MaxBulkReconciliationRefs = 100_000;

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IExpectedBookRepository _expectedBookRepository;
    private readonly ILogger<AuthorReconciliationProvider> _logger;

    public AuthorReconciliationProvider(
        IAudiobookRepository audiobookRepository,
        IExpectedBookRepository expectedBookRepository,
        ILogger<AuthorReconciliationProvider> logger)
    {
        _audiobookRepository = audiobookRepository;
        _expectedBookRepository = expectedBookRepository;
        _logger = logger;
    }

    public async Task<AuthorReconciliation> GetReconciliationAsync(long personId, bool includeMissingSeries = true)
    {
        var (expected, rosterOverflow) = await _expectedBookRepository.GetByAuthorBoundedAsync(
            personId, MaxReconciliationRosterEntries);
        if (rosterOverflow)
        {
            throw new InvalidOperationException(
                $"Author {personId} has at least {MaxReconciliationRosterEntries + 1} roster entries, exceeding the {MaxReconciliationRosterEntries} the detail view reconciles.");
        }

        var (ownedKeys, ownedOverflow) = await _audiobookRepository.GetOwnedKeysByAuthorAsync(
            personId, MaxReconciliationOwnedKeys);
        if (ownedOverflow)
        {
            throw new InvalidOperationException(
                $"Author {personId} has at least {MaxReconciliationOwnedKeys + 1} owned books, exceeding the {MaxReconciliationOwnedKeys} the detail view reconciles.");
        }

        var ownedIndex = new AuthorOwnedIndex(ownedKeys);
        var active = expected.Where(e => !e.IsIgnored).ToList();
        var ignored = expected
            .Where(e => e.IsIgnored)
            .Select(ToExpectedInfo)
            .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var unmatched = active
            .Where(e => !IsOwned(MatchKey.From(e), ownedIndex))
            .ToList();

        var missing = unmatched
            .Where(e => !ExpectedBookClassifier.IsUpcoming(e.ReleaseDate, e.Year, today))
            .Select(ToExpectedInfo)
            .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

        var upcoming = unmatched
            .Where(e => ExpectedBookClassifier.IsUpcoming(e.ReleaseDate, e.Year, today))
            .Select(ToExpectedInfo)
            .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

        return new AuthorReconciliation(
            missing, upcoming, ignored,
            ExpectedBookCount: active.Count, OwnedCount: ownedKeys.Count,
            MissingSeries: includeMissingSeries
                ? ComputeMissingSeries(active, ownedIndex)
                : new List<AuthorMissingSeriesInfo>());
    }

    /// <inheritdoc cref="IAuthorReconciliationProvider.GetBulkMissingOrUpcomingAuthorIdsAsync"/>
    public async Task<AuthorBulkReconciliationResult> GetBulkMissingOrUpcomingAuthorIdsAsync()
    {
        // The whole-library input is read through a single bounded query (cap + 1 refs with an
        // overflow flag); the per-author classification below then bounds both axes again (an
        // author's own refs and owned books against the same caps the detail view enforces). A
        // library past the global cap is refused - the affected filter is skipped by the caller
        // rather than silently truncated into a wrong result.
        var (refs, overflow) = await _expectedBookRepository.GetActiveAuthorBookRefsAsync(MaxBulkReconciliationRefs);
        if (overflow)
        {
            return new AuthorBulkReconciliationResult(new HashSet<long>(), new HashSet<long>(), Refused: true);
        }

        var hasMissing = new HashSet<long>();
        var hasUpcoming = new HashSet<long>();
        if (refs.Count == 0)
        {
            return new AuthorBulkReconciliationResult(hasMissing, hasUpcoming, Refused: false);
        }

        // ONE batched read serves every rostered author's owned keys instead of one query per
        // author - hundreds of matched authors used to mean that many sequential round trips per
        // page load of the authors list (the N+1 the pre-PR batched call avoided). The bound is
        // the largest total that still guarantees every requested author's keys below it are
        // COMPLETE: person count times the per-author cap plus one. Under it, the per-author cap
        // checked in the loop below is exact (an author past MaxReconciliationOwnedKeys is skipped
        // exactly like the detail view refuses it); an overflow means the flat bound cut some
        // author's keys mid-list, so a prefix cannot be trusted for ANY author and the filter is
        // refused rather than classifying from a short list.
        var groups = refs.GroupBy(r => r.PersonId).ToList();
        // Authors past the roster cap are skipped below before classification, so their owned
        // keys would only spend the batched read's budget on a row the loop ignores.
        var personIdsForOwnedKeys = groups
            .Where(g => g.Count() <= MaxReconciliationRosterEntries)
            .Select(g => g.Key)
            .ToList();

        // The flat total cap is person count times the per-author cap plus one, computed in
        // <see cref="long"/> because the intermediate product overflows an int already past
        // ~107k authors - and the deliberate refusal below is why that matters. If the true total
        // cannot be expressed as an int cap, the filter is refused up front (with a log) rather
        // than clamped: the old clamp handed the repository an int.MaxValue cap whose
        // Take(cap + 1) probe overflowed back, silently turning a genuinely enormous but
        // per-author-legitimate roster into an empty/truncated read.
        var computedCap = ComputeTotalKeyCap(personIdsForOwnedKeys.Count);
        if (computedCap is null)
        {
            _logger.LogWarning(
                "Refusing the bulk authors filter: the batched owned-key read for {PersonCount} rostered authors needs a flat total cap above the largest int the query can take.",
                personIdsForOwnedKeys.Count);
            return new AuthorBulkReconciliationResult(new HashSet<long>(), new HashSet<long>(), Refused: true);
        }

        var (ownedKeys, ownedOverflow) = await _audiobookRepository.GetOwnedKeysByAuthorsAsync(
            personIdsForOwnedKeys, computedCap.Value);
        if (ownedOverflow)
        {
            return new AuthorBulkReconciliationResult(new HashSet<long>(), new HashSet<long>(), Refused: true);
        }

        var ownedKeysByAuthor = ownedKeys
            .GroupBy(k => k.PersonId)
            .ToDictionary(g => g.Key, g => g.Select(k => k.Key).ToList());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var group in groups)
        {
            // Same matching and Missing-vs-Upcoming classification (SeriesRosterMatcher +
            // ExpectedBookClassifier) as GetReconciliationAsync, just batched across every author
            // with a roster entry instead of one author at a time. GetReconciliationAsync enforces
            // MaxReconciliationRosterEntries/MaxReconciliationOwnedKeys per author and throws on
            // overflow - the right response for a single detail-page request. This is a
            // list-filter endpoint that classifies every author with a roster in one pass, so
            // throwing here would take the whole authors list down over one pathological author.
            // Instead, an author past either cap is left out of both result sets (silently
            // unclassifiable to the filter, exactly like the detail view refuses to reconcile it).
            var entries = group.ToList();
            if (entries.Count > MaxReconciliationRosterEntries)
            {
                continue;
            }

            var authorKeys = ownedKeysByAuthor.GetValueOrDefault(group.Key, new List<SeriesOwnedKey>());
            if (authorKeys.Count > MaxReconciliationOwnedKeys)
            {
                continue;
            }

            var ownedIndex = new AuthorOwnedIndex(authorKeys);
            foreach (var bookRef in entries)
            {
                if (IsOwned(MatchKey.From(bookRef), ownedIndex))
                {
                    continue;
                }

                if (ExpectedBookClassifier.IsUpcoming(bookRef.ReleaseDate, bookRef.Year, today))
                {
                    hasUpcoming.Add(group.Key);
                }
                else
                {
                    hasMissing.Add(group.Key);
                }
            }
        }

        return new AuthorBulkReconciliationResult(hasMissing, hasUpcoming, Refused: false);
    }

    /// <summary>
    /// The flat total cap for the batched owned-key read
    /// (<see cref="IAudiobookRepository.GetOwnedKeysByAuthorsAsync"/>): every requested author's
    /// key set must be complete below it, so it is exactly the person count times the per-author
    /// cap plus one - computed in <see cref="long"/> because the intermediate product overflows an
    /// <see cref="int"/> already past ~107k rostered authors. Returns null when the true total
    /// exceeds <c>int.MaxValue - 1</c>, the largest cap the repository's
    /// <c>Take(cap + 1)</c> probe can express without overflowing back (its own bound is an int),
    /// and the caller should refuse the filter deliberately rather than clamp to a truncated read.
    /// </summary>
    internal static int? ComputeTotalKeyCap(int rosterPersonCount)
    {
        var total = (long)rosterPersonCount * (MaxReconciliationOwnedKeys + 1);
        return total > int.MaxValue - 1 ? null : (int)total;
    }

    // --- Matching -------------------------------------------------------------

    /// <summary>
    /// The per-author owned-book index the reconciliation matches against. Series entries are
    /// scoped to the owned books of the same local series value (the catalog row's Name equals the
    /// <see cref="Audiobook.Series"/> tag value verbatim, so an exact trimmed comparison is the
    /// right scoping key); standalone and unmatched-source-series entries fall back to the flat
    /// index over everything the author owns.
    /// </summary>
    private sealed class AuthorOwnedIndex
    {
        private readonly SeriesRosterMatcher.OwnedBookIndex _all;
        private readonly Dictionary<string, SeriesRosterMatcher.OwnedBookIndex> _bySeries;
        private readonly Dictionary<string, int> _ownedCountBySeries;

        public AuthorOwnedIndex(IEnumerable<SeriesOwnedKey> ownedKeys)
        {
            var keys = ownedKeys.ToList();
            _all = new SeriesRosterMatcher.OwnedBookIndex(keys);
            _bySeries = keys
                .GroupBy(k => k.Series?.Trim() ?? string.Empty)
                .ToDictionary(g => g.Key, g => new SeriesRosterMatcher.OwnedBookIndex(g.ToList()), StringComparer.Ordinal);
            _ownedCountBySeries = keys
                .Where(k => k.Series is not null && k.Series!.Trim() != string.Empty)
                .GroupBy(k => k.Series!.Trim())
                .ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>Title-only match against any owned book of the author (a standalone entry).</summary>
        public bool ContainsStandalone(string title) => _all.Contains(SeriesRosterMatcher.BookKey.From(null, title));

        /// <summary>Best-effort position/title match against any owned book of the author (an unmatched source-series entry).</summary>
        public bool ContainsPositional(string? position, string title) =>
            _all.Contains(SeriesRosterMatcher.BookKey.From(position, title));

        /// <summary>Position/title match scoped to the owned books of one local series value, or false when the author owns nothing in it.</summary>
        public bool ContainsSeries(string localSeriesName, string? position, string title)
        {
            var index = _bySeries.GetValueOrDefault(localSeriesName.Trim());
            return index is not null && index.Contains(SeriesRosterMatcher.BookKey.From(position, title));
        }

        /// <summary>How many of the author's owned books sit in this local series value (0 for a value the author owns nothing in).</summary>
        public int OwnedCountInSeries(string localSeriesName) => _ownedCountBySeries.GetValueOrDefault(localSeriesName.Trim(), 0);
    }

    /// <summary>
    /// The reduced per-entry matching key - the same shape the detail view derives from an
    /// <see cref="ExpectedBook"/> and the bulk filter from an <see cref="ExpectedBookAuthorBookRef"/>,
    /// so both classifiers share one <see cref="IsOwned(MatchKey, AuthorOwnedIndex)"/>.
    /// <see cref="HasLocalSeries"/> is true when the entry is linked to a catalog series (the
    /// <see cref="ExpectedBook.SeriesId"/> link, or - for the ref projection - a populated local
    /// series name, which is only carried for linked books).
    /// </summary>
    private sealed record MatchKey(bool HasLocalSeries, string? LocalSeriesName, string? SourceSeriesId, string? Position, string Title)
    {
        public static MatchKey From(ExpectedBook book) =>
            new(book.SeriesId is not null, book.Series?.Name, book.SourceSeriesId, book.SeriesPosition, book.Title);

        public static MatchKey From(ExpectedBookAuthorBookRef bookRef) =>
            new(bookRef.SeriesName is not null, bookRef.SeriesName, bookRef.SourceSeriesId, bookRef.SeriesPart, bookRef.Title);
    }

    /// <summary>
    /// Whether any owned book of the author corresponds to this roster entry - see the class doc
    /// for the series-scoping rules. A book already owned must never surface as missing/upcoming.
    /// </summary>
    private static bool IsOwned(MatchKey entry, AuthorOwnedIndex owned)
    {
        if (entry.HasLocalSeries && entry.LocalSeriesName is not null && entry.LocalSeriesName.Trim() != string.Empty)
        {
            return owned.ContainsSeries(entry.LocalSeriesName, entry.Position, entry.Title);
        }

        if (!string.IsNullOrEmpty(entry.SourceSeriesId))
        {
            return owned.ContainsPositional(entry.Position, entry.Title);
        }

        return owned.ContainsStandalone(entry.Title);
    }

    // --- Missing-series computation ------------------------------------------

    /// <summary>
    /// The distinct source-series identity a missing-series group is keyed on.
    /// </summary>
    private sealed record SeriesGroupKey(string SourceName, string SourceSeriesId);

    /// <summary>
    /// The author's distinct source-series groups whose local library owns zero books in the
    /// matched local series (or whose source series is not yet matched locally at all), with the
    /// per-group missing/upcoming counts. A series with some owned books is NOT reported here, but
    /// its unmatched books stay in the Missing/Upcoming lists and become reachable through the
    /// series view once the series is matched. Bounded before materialization: the group count is
    /// capped at <see cref="MaxAuthorSeriesGroups"/> and refuses past it.
    /// </summary>
    private static List<AuthorMissingSeriesInfo> ComputeMissingSeries(
        List<ExpectedBook> active, AuthorOwnedIndex ownedIndex)
    {
        // Materialized ONCE: GroupBy is lazy, so `groups` is an Iterable of groupings - counting
        // it (twice below) or iterating it again would each re-run the whole grouping pipeline
        // over `active`. The list is what the cap check and the loop both read from.
        var groups = active
            .Where(e => !string.IsNullOrEmpty(e.SourceSeriesId))
            .GroupBy(e => new SeriesGroupKey(e.SourceName, e.SourceSeriesId!))
            .ToList();

        if (groups.Count > MaxAuthorSeriesGroups)
        {
            throw new InvalidOperationException(
                $"Author has {groups.Count} distinct source series, exceeding the {MaxAuthorSeriesGroups} the detail view reports.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = new List<AuthorMissingSeriesInfo>();
        foreach (var group in groups)
        {
            var entries = group.ToList();
            var firstWithSeries = entries.FirstOrDefault(e => e.SeriesId is not null) ?? entries.First();

            var missingCount = 0;
            var upcomingCount = 0;
            foreach (var entry in entries)
            {
                if (IsOwned(MatchKey.From(entry), ownedIndex))
                {
                    continue;
                }

                if (ExpectedBookClassifier.IsUpcoming(entry.ReleaseDate, entry.Year, today))
                {
                    upcomingCount++;
                }
                else
                {
                    missingCount++;
                }
            }

            var seriesName = firstWithSeries.Series?.Name;
            var ownedCount = seriesName is null ? 0 : ownedIndex.OwnedCountInSeries(seriesName);
            if (ownedCount > 0)
            {
                continue;
            }

            result.Add(new AuthorMissingSeriesInfo
            {
                SourceName = group.Key.SourceName,
                SourceSeriesId = group.Key.SourceSeriesId,
                SourceSeriesName = entries.Select(e => e.SourceSeriesName).FirstOrDefault(n => n is not null && n.Trim() != string.Empty),
                SeriesId = firstWithSeries.SeriesId,
                SeriesName = seriesName,
                ExpectedCount = entries.Count,
                MissingCount = missingCount,
                UpcomingCount = upcomingCount,
                OwnedCount = ownedCount,
            });
        }

        return result
            .OrderBy(g => g.SourceSeriesName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.SourceSeriesId)
            .ToList();
    }

    internal static AuthorExpectedBookInfo ToExpectedInfo(ExpectedBook book) => new()
    {
        Id = book.Id,
        Title = book.Title,
        Year = book.Year,
        ReleaseDate = book.ReleaseDate,
        SourceUrl = book.SourceUrl,
        IsIgnored = book.IsIgnored,
        SourceName = book.SourceName,
        SourceBookId = book.SourceBookId,
        ImageUrl = book.ImageUrl,
        Position = book.SeriesPosition,
        SeriesId = book.SeriesId,
        SeriesName = book.Series?.Name,
        SourceSeriesId = book.SourceSeriesId,
        SourceSeriesName = book.SourceSeriesName,
    };
}