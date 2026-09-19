using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// The series-refresh diff: the explicit changes between a freshly fetched source roster and the
/// series' currently owned books. Two change kinds together describe everything the review
/// dialog can act on:
///
/// <list type="bullet">
/// <item><see cref="SeriesRefreshChangeType.MissingBook"/> is deliberately never produced any
/// more - a roster entry no owned book matches is already persistently visible via the
/// roster/reconciliation's Missing/Upcoming sections (see <c>SeriesReconciliation</c>), so a
/// refresh no longer re-surfaces it as a "pending change" to review. The enum member is kept only
/// so a <c>PendingSeriesRefresh</c> row written before this change still deserializes; new rows
/// never carry it. The ignore carry-across for missing entries still happens, but on the roster
/// replace itself (see <c>SeriesService.MatchSeriesCoreAsync</c>), not here.</item>
/// <item><see cref="SeriesRefreshChangeType.PartUpdate"/> - an owned book that matches a roster
/// entry with a position, whose stored part agrees with none of its matched entries. The source
/// renumbered the book (a book stored as part "01" that the source now positions at "02").</item>
/// <item><see cref="SeriesRefreshChangeType.PartRemoval"/> - an owned book that carries a part
/// while no roster entry matches it at all. The source no longer lists the book, so the part no
/// longer has a basis.</item>
/// </list>
///
/// The matching is the same rule the reconciliation applies (because it IS the same rule -
/// <see cref="SeriesRosterMatcher"/>), so a refresh never calls a book "missing" that the detail
/// page would call owned, and never proposes an update that contradicts a part mismatch the
/// detail page reports against a different entry.
///
/// Deliberately conservative where the two features overlap:
/// <list type="bullet">
/// <item>A book with NO stored part is never proposed an update or a removal. First-time part
/// assignment stays on the detail page's part-mismatch flow, where a human confirms it against
/// the roster; refresh proposes renumbering and removals only.</item>
/// <item>A book whose part agrees with any of its matched entries is not a candidate - the same
/// "agree with any entry, report against the earliest-position match" attribution the
/// reconciliation applies to part mismatches, so a book that matches both an omnibus edition and
/// its own individual entry is never told to chase the other one.</item>
/// </list>
/// </summary>
internal static class SeriesRefreshDiffer
{
    /// <summary>
    /// Computes the explicit changes between <paramref name="roster"/> and the series' owned
    /// books. The ordering is presentation order within each kind: part updates by the roster's
    /// own position order (blanks last, mirroring the detail page), removals by the part being
    /// removed.
    /// </summary>
    public static IReadOnlyList<SeriesRefreshChange> Diff(
        IReadOnlyList<SeriesRefreshRosterEntry> roster,
        IReadOnlyList<SeriesOwnedKey> ownedKeys,
        bool includeOmnibusEditions)
    {
        var visible = roster
            .Where(e => includeOmnibusEditions || !e.IsCompilation)
            .ToList();
        var ownedIndex = new SeriesRosterMatcher.OwnedBookIndex(ownedKeys);
        var ownedKeyById = ownedKeys.ToDictionary(k => k.AudiobookId);

        // Attribution is per book over ALL the entries it matches, exactly like the
        // reconciliation's part-mismatch pass, so a duplicate-title roster (an omnibus and the
        // individual editions of one book) cannot make the same book chase two positions.
        //
        // A roster entry no owned book matches is deliberately NOT reported here any more - it
        // is already persistently visible via the roster/reconciliation's Missing/Upcoming
        // sections (see SeriesReconciliation), so re-surfacing it as a "pending change" to review
        // would ask the user to act on the same information twice. SeriesRefreshChangeType still
        // carries a MissingBook member purely so a historical PendingSeriesRefresh row (written
        // before this change) still deserializes - nothing here constructs one any more.
        var matchesByBook = new Dictionary<long, List<SeriesRefreshRosterEntry>>();
        foreach (var entry in visible)
        {
            var key = SeriesRosterMatcher.BookKey.From(entry.Position, entry.Title);
            var matches = ownedIndex.FindMatches(key);
            if (matches.Count == 0)
            {
                continue;
            }

            foreach (var owned in matches)
            {
                if (!matchesByBook.TryGetValue(owned.AudiobookId, out var entries))
                {
                    entries = new List<SeriesRefreshRosterEntry>();
                    matchesByBook[owned.AudiobookId] = entries;
                }

                entries.Add(entry);
            }
        }

        var partUpdates = new List<SeriesRefreshChange>();
        var partRemovals = new List<SeriesRefreshChange>();
        foreach (var (audiobookId, matchedEntries) in matchesByBook)
        {
            if (!ownedKeyById.TryGetValue(audiobookId, out var owned) ||
                string.IsNullOrWhiteSpace(owned.SeriesPart))
            {
                continue;
            }

            // A part that agrees with any matched entry is fine - never chase another entry's
            // position (the omnibus/individual duplicate-title case).
            if (matchedEntries.Any(e =>
                    !string.IsNullOrWhiteSpace(e.Position) &&
                    SeriesRosterMatcher.PartsEquivalent(owned.SeriesPart, e.Position)))
            {
                continue;
            }

            // An update needs a roster-assigned position to fix against. A book matched ONLY by
            // positionless entries has a part with no basis - the source assigns no position to
            // anything matching it - which is a removal, not an update.
            var attributed = matchedEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.Position))
                .OrderBy(e => SeriesRosterMatcher.PositionSortKey(e.Position))
                .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (attributed is null)
            {
                partRemovals.Add(new SeriesRefreshChange(
                    SeriesRefreshChangeType.PartRemoval,
                    audiobookId,
                    owned.BookName,
                    owned.SeriesPart,
                    NewPart: null,
                    RosterTitle: null,
                    Position: null,
                    Title: null,
                    Year: null));
                continue;
            }

            partUpdates.Add(new SeriesRefreshChange(
                SeriesRefreshChangeType.PartUpdate,
                audiobookId,
                owned.BookName,
                owned.SeriesPart,
                attributed.Position,
                attributed.Title,
                Position: null,
                Title: null,
                Year: null));
        }

        var matchedIds = new HashSet<long>(matchesByBook.Keys);
        foreach (var owned in ownedKeys)
        {
            if (matchedIds.Contains(owned.AudiobookId) || string.IsNullOrWhiteSpace(owned.SeriesPart))
            {
                continue;
            }

            partRemovals.Add(new SeriesRefreshChange(
                SeriesRefreshChangeType.PartRemoval,
                owned.AudiobookId,
                owned.BookName,
                owned.SeriesPart,
                NewPart: null,
                RosterTitle: null,
                Position: null,
                Title: null,
                Year: null));
        }

        return partUpdates
            .OrderBy(c => SeriesRosterMatcher.PositionSortKey(c.NewPart))
            .ThenBy(c => c.BookName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.AudiobookId)
            .Concat(partRemovals
                .OrderBy(c => SeriesRosterMatcher.PositionSortKey(c.StoredPart))
                .ThenBy(c => c.BookName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.AudiobookId))
            .ToList();
    }
}