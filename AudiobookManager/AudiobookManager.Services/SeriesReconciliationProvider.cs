using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// The reconciliation computation itself. See <see cref="ISeriesReconciliationProvider"/> for why
/// it lives here rather than on <c>SeriesService</c> - it is the half of the series logic the
/// consistency graph needs, separated from the half that needs the consistency graph.
/// </summary>
public class SeriesReconciliationProvider : ISeriesReconciliationProvider
{
    /// <summary>
    /// The largest roster the reconciliation will classify in memory. The roster is stored from a
    /// metadata source's series page, so this is a defensive floor far above any real source
    /// (Hardcover returns one series at a time). It is enforced BEFORE materialization: the
    /// repository fetch is bounded to cap + 1 rows, so a pathological roster is detected and
    /// refused without ever being loaded whole. It is a hard bound on purpose - a series past it
    /// indicates corrupted data, and failing loudly beats showing wrong missing counts.
    /// </summary>
    public const int MaxReconciliationRosterEntries = 5_000;

    /// <summary>
    /// The largest owned-book key set (per series) the reconciliation will classify against. Same
    /// rationale as <see cref="MaxReconciliationRosterEntries"/>: the fuzzy matching is
    /// O(roster x owned), so both sides must be explicitly bounded for the computation to be
    /// genuinely bounded rather than merely usually-small - and the owned-key fetch is bounded to
    /// cap + 1 rows so an oversized set is detected without materializing it.
    /// </summary>
    public const int MaxReconciliationOwnedKeys = 20_000;

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly ISeriesReconciliationCache _reconciliationCache;

    public SeriesReconciliationProvider(
        IAudiobookRepository audiobookRepository,
        ISeriesRepository seriesRepository,
        ISeriesReconciliationCache reconciliationCache)
    {
        _audiobookRepository = audiobookRepository;
        _seriesRepository = seriesRepository;
        _reconciliationCache = reconciliationCache;
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

        var expected = catalogRow?.ExpectedBooks ?? new List<ExpectedBook>();

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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var unmatched = active.Where(e => !IsOwned(e, ownedIndex)).ToList();

        var missing = unmatched
            .Where(e => !ExpectedBookClassifier.IsUpcoming(e.ReleaseDate, e.Year, today))
            .Select(ToExpectedInfo)
            .OrderBy(e => SeriesRosterMatcher.PositionSortKey(e.Position))
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

        var upcoming = unmatched
            .Where(e => ExpectedBookClassifier.IsUpcoming(e.ReleaseDate, e.Year, today))
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

        // The dismissed rows keep the SAME today/classifier as the active split above, so the
        // detail page's per-section ignored sub-lists can never disagree with the Missing/Upcoming
        // sections they render inside of.
        var ignoredMissing = ignored
            .Where(i => !ExpectedBookClassifier.IsUpcoming(i.ReleaseDate, i.Year, today))
            .ToList();
        var ignoredUpcoming = ignored
            .Where(i => ExpectedBookClassifier.IsUpcoming(i.ReleaseDate, i.Year, today))
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
            .Where(e => !string.IsNullOrWhiteSpace(e.SeriesPosition))
            .OrderBy(e => SeriesRosterMatcher.PositionSortKey(e.SeriesPosition))
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

        var matchesByBook = new Dictionary<long, List<ExpectedBook>>();
        foreach (var entry in entries)
        {
            foreach (var owned in ownedIndex.FindMatches(SeriesRosterMatcher.BookKey.From(entry.SeriesPosition, entry.Title)))
            {
                if (!matchesByBook.TryGetValue(owned.AudiobookId, out var bookEntries))
                {
                    bookEntries = new List<ExpectedBook>();
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

            if (matchesByBook[audiobookId].Any(e => SeriesRosterMatcher.PartsEquivalent(owned.SeriesPart, e.SeriesPosition)))
            {
                continue;
            }

            var attributed = matchesByBook[audiobookId].First();
            partMismatches.Add(new SeriesPartMismatch
            {
                AudiobookId = owned.AudiobookId,
                BookName = owned.BookName,
                StoredPart = owned.SeriesPart,
                ExpectedPart = attributed.SeriesPosition!,
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
            authors,
            upcoming,
            ignoredMissing,
            ignoredUpcoming);
    }

    internal static SeriesExpectedBookInfo ToExpectedInfo(ExpectedBook book) => new()
    {
        Id = book.Id,
        Title = book.Title,
        Position = book.SeriesPosition,
        Year = book.Year,
        ReleaseDate = book.ReleaseDate,
        SourceUrl = book.SourceUrl,
        IsIgnored = book.IsIgnored,
        SourceName = book.SourceName,
        SourceBookId = book.SourceBookId,
        ImageUrl = book.ImageUrl,
    };

    /// <summary>
    /// Whether any owned book corresponds to this roster entry. Owned book names rarely match
    /// a source title byte-for-byte, so an exact position match counts, and otherwise titles
    /// are compared fuzzily - the shared rule in <see cref="SeriesRosterMatcher"/>.
    /// </summary>
    internal static bool IsOwned(ExpectedBook expected, SeriesRosterMatcher.OwnedBookIndex ownedBooks) =>
        ownedBooks.Contains(SeriesRosterMatcher.BookKey.From(expected.SeriesPosition, expected.Title));
}
