using AudiobookManager.Database.Repositories;
using AudiobookManager.Database.Search;
using AudiobookManager.Domain;
using AudiobookManager.Services.Similarity;

namespace AudiobookManager.Services;

/// <summary>
/// The single "is this roster entry the same book as that owned book" matching logic, shared by
/// every consumer that classifies a series' roster against its owned books so none of them can
/// drift apart:
///
/// <list type="bullet">
/// <item><see cref="SeriesService.ComputeReconciliationAsync"/> (the detail page's missing /
/// ignored / part-mismatch sections and the library-wide consistency detector), and</item>
/// <item>the series refresh diff (part updates, missing source books and part removals a pending
/// refresh surfaces).</item>
/// </list>
///
/// A roster entry and an owned book are the same book when their positions are equivalent
/// (numeric parts compare by value, free text by trimmed case-insensitive text - "2" and "2.0"
/// are the same part) and their titles are non-contradictory, or when their titles alone are
/// close enough (a position is only corroborating, never disqualifying, because users mistype
/// them). This is deliberately the same rule the reconciliation applied before the extraction -
/// the extraction moved the code, it did not change the semantics.
/// </summary>
internal static class SeriesRosterMatcher
{
    /// <summary>
    /// Minimum normalized title similarity for an owned book to be considered the same book
    /// as a roster entry when positions don't settle it.
    /// </summary>
    internal const double TitleMatchThreshold = 0.85;

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
    /// A book reduced to what the owned/expected comparison needs, with its title normalized
    /// once up front - the matching loop is O(expected x owned), so re-normalizing per
    /// comparison would repeat the same work for every candidate.
    /// </summary>
    internal readonly record struct BookKey(string? Position, string NormalizedTitle)
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
    internal sealed class OwnedBookIndex
    {
        private readonly List<OwnedBookIndexItem> _items;
        private readonly ILookup<string, OwnedBookIndexItem> _byPosition;

        public OwnedBookIndex(IEnumerable<SeriesOwnedKey> ownedKeys)
        {
            _items = ownedKeys.Select(k => new OwnedBookIndexItem(k, BookKey.From(k.SeriesPart, k.BookName))).ToList();
            _byPosition = _items
                .Where(i => !string.IsNullOrWhiteSpace(i.Key.SeriesPart))
                .ToLookup(i => NormalizePosition(i.Key.SeriesPart!), StringComparer.OrdinalIgnoreCase);
        }

        public bool Contains(BookKey expected)
        {
            if (!string.IsNullOrWhiteSpace(expected.Position))
            {
                foreach (var item in _byPosition[NormalizePosition(expected.Position!)])
                {
                    if (IsSameBook(expected, item.BookKey))
                    {
                        return true;
                    }
                }
            }

            return _items.Any(item => IsSameBook(expected, item.BookKey));
        }

        /// <summary>
        /// Every owned book this roster entry corresponds to. A book can match more than one
        /// roster entry (duplicate titles across positions), so the callers that need exactly one
        /// answer (<see cref="Contains"/>) take "any"; the part-mismatch pass reports each matched
        /// book and deduplicates itself.
        /// </summary>
        public List<SeriesOwnedKey> FindMatches(BookKey expected)
        {
            var matches = new List<SeriesOwnedKey>();

            // Fast path only - never a substitute for the scan below.
            if (!string.IsNullOrWhiteSpace(expected.Position))
            {
                foreach (var item in _byPosition[NormalizePosition(expected.Position!)])
                {
                    if (IsSameBook(expected, item.BookKey))
                    {
                        matches.Add(item.Key);
                    }
                }
            }

            foreach (var item in _items)
            {
                if (IsSameBook(expected, item.BookKey)
                    && !matches.Contains(item.Key))
                {
                    matches.Add(item.Key);
                }
            }

            return matches;
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

    private sealed record OwnedBookIndexItem(SeriesOwnedKey Key, BookKey BookKey);

    /// <summary>
    /// Whether an owned book's stored part agrees with the position of the roster entry it was
    /// matched to. A stored empty part is a genuine miss and a stored one is compared with the
    /// same equivalence matching applies - "2" and "2.0" are the same part, "2" and "7" are not.
    /// </summary>
    internal static bool PartsEquivalent(string? storedPart, string? expectedPosition) =>
        !string.IsNullOrWhiteSpace(storedPart) && PositionsEqual(storedPart, expectedPosition);

    internal static bool IsSameBook(BookKey expected, BookKey owned)
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

    internal static bool TitlesNotContradictory(string normA, string normB)
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

    internal static bool PositionsEqual(string? a, string? b) =>
        SeriesPartEquivalence.PartsEquivalentClr(a, b);

    /// <summary>
    /// 0..1 similarity of two free-text values, using the shared comparison-only normalizer
    /// and edit distance scaled by the longer string's length.
    /// </summary>
    internal static double TitleSimilarity(string? a, string? b) =>
        NormalizedSimilarity(NameNormalizer.Normalize(a), NameNormalizer.Normalize(b));

    /// <summary>
    /// Similarity of two already-normalized strings. When the caller only cares whether the
    /// score reaches <paramref name="threshold"/>, the length difference (a lower bound on
    /// the edit distance) can rule the pair out before the O(n*m) distance matrix is built.
    /// The threshold stays here rather than inside LevenshteinDistance, which is
    /// general-purpose.
    /// </summary>
    internal static double NormalizedSimilarity(string normA, string normB, double threshold = 0)
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
    /// The total-order key a roster's positions sort by for display: numeric parts by value,
    /// non-numeric parts after them alphabetically, blank parts last. Mirrors the SQL
    /// <c>SeriesPartSortKey</c> the owned-books page orders by.
    /// </summary>
    internal static (double Numeric, string Text) PositionSortKey(string? position)
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