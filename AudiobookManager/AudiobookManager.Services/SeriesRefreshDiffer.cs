using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// The series-refresh diff: the explicit changes between a freshly fetched source roster and the
/// series' currently owned books. Three change kinds together describe everything the review
/// dialog can act on:
///
/// <list type="bullet">
/// <item><see cref="SeriesRefreshChangeType.MissingBook"/> - a visible roster entry no owned
/// book matches. The source knows a book the user does not. Entries the user has already
/// ignored are deliberately NOT missing: they are excluded from the visible series on the
/// detail page, so the diff excludes them too via the <paramref name="previouslyIgnored"/>
/// natural keys, using the same same-book rule the roster replace carries the flags across
/// with. An entry the ignore decision covers is not a change, whatever else the refresh found.</item>
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
    /// books. <paramref name="previouslyIgnored"/> is the set of roster entries (as book keys)
    /// the user has already ignored in the stored roster; any fresh entry that is recognisably
    /// the same book as one of them is not reported missing. The ordering is presentation order
    /// within each kind: missing books and part updates by the roster's own position order
    /// (blanks last, mirroring the detail page), removals by the part being removed.
    /// </summary>
    public static IReadOnlyList<SeriesRefreshChange> Diff(
        IReadOnlyList<SeriesRefreshRosterEntry> roster,
        IReadOnlyList<SeriesOwnedKey> ownedKeys,
        bool includeOmnibusEditions,
        IReadOnlyList<SeriesRosterMatcher.BookKey>? previouslyIgnored = null)
    {
        var visible = roster
            .Where(e => includeOmnibusEditions || !e.IsCompilation)
            .ToList();
        var ownedIndex = new SeriesRosterMatcher.OwnedBookIndex(ownedKeys);
        var ownedKeyById = ownedKeys.ToDictionary(k => k.AudiobookId);

        // Attribution is per book over ALL the entries it matches, exactly like the
        // reconciliation's part-mismatch pass, so a duplicate-title roster (an omnibus and the
        // individual editions of one book) cannot make the same book chase two positions.
        var matchesByBook = new Dictionary<long, List<SeriesRefreshRosterEntry>>();
        var missing = new List<SeriesRefreshChange>();
        foreach (var entry in visible)
        {
            var key = SeriesRosterMatcher.BookKey.From(entry.Position, entry.Title);
            var matches = ownedIndex.FindMatches(key);
            if (matches.Count == 0)
            {
                // Only entries that WOULD be reported missing pay for the ignored-set scan, and
                // the exemption uses the same same-book rule that carries the flags across the
                // roster replace - so a source-renumbered (but recognisably the same) ignored
                // entry stays quiet, and checking an entry the user owns costs nothing extra.
                if (previouslyIgnored is not null &&
                    previouslyIgnored.Any(p => SeriesRosterMatcher.IsSameBook(p, key)))
                {
                    continue;
                }

                missing.Add(new SeriesRefreshChange(
                    SeriesRefreshChangeType.MissingBook,
                    AudiobookId: null,
                    BookName: null,
                    StoredPart: null,
                    NewPart: null,
                    RosterTitle: null,
                    Position: entry.Position,
                    Title: entry.Title,
                    Year: entry.Year));
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

        return missing
            .OrderBy(c => SeriesRosterMatcher.PositionSortKey(c.Position))
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .Concat(partUpdates
                .OrderBy(c => SeriesRosterMatcher.PositionSortKey(c.NewPart))
                .ThenBy(c => c.BookName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.AudiobookId))
            .Concat(partRemovals
                .OrderBy(c => SeriesRosterMatcher.PositionSortKey(c.StoredPart))
                .ThenBy(c => c.BookName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.AudiobookId))
            .ToList();
    }
}