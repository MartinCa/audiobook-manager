using AudiobookManager.Database.Search;
using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// A book's series as a metadata source reports them, canonicalised and compared against what a
/// stored book has - the one implementation the refresh differ, the applier and the pending
/// snapshot share, so what is offered as a change and what an apply writes cannot disagree.
///
/// Sources report several series, in an order that is presentation (Audible's markup against its
/// JSON, Hardcover's featured series against its list), so nothing here depends on it: entries are
/// canonicalised (trimmed, blanks dropped, the same series listed twice merged) and compared as
/// sets. A source has no notion of "primary", so the primary is chosen by <see cref="ChoosePrimary"/>
/// - deterministically, at the moment a snapshot is diffed or applied, which is what lets bulk and
/// background refreshes pick one without asking anyone.
/// </summary>
public static class SeriesRelationSet
{
    /// <summary>One series of a book: name, optional part, and the pre-mapping name a source reported.</summary>
    public sealed record Entry(string Name, string? Part, string? OriginalName = null);

    /// <summary>The series a book ends up with: one primary (absent only when there are none) and the rest.</summary>
    public sealed record Resolved(Entry? Primary, IReadOnlyList<Entry> Additional)
    {
        public IEnumerable<Entry> All => Primary is null ? Additional : Additional.Prepend(Primary);
    }

    /// <summary>
    /// Trims names and parts, drops blank names and merges the same name listed twice
    /// (case-insensitively - a mapping can send two entries to one target), keeping the first
    /// non-empty part. Ordered by name, so the source's own ordering never matters.
    /// </summary>
    public static IReadOnlyList<Entry> Canonicalize(IEnumerable<Entry>? entries)
    {
        var merged = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries ?? Enumerable.Empty<Entry>())
        {
            var name = entry.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var part = string.IsNullOrWhiteSpace(entry.Part) ? null : entry.Part.Trim();
            if (merged.TryGetValue(name, out var existing))
            {
                if (existing.Part is null && part is not null)
                {
                    merged[name] = existing with { Part = part };
                }
            }
            else
            {
                merged[name] = new Entry(name, part, entry.OriginalName);
            }
        }

        return merged.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Picks the primary from a source's series, given the series the book has now (primary first):
    /// the explicit choice if the caller made one and the source has it; otherwise the book's
    /// current primary if the source still lists it; otherwise the first of the book's other
    /// series the source lists; otherwise the source's first. Keeping the current primary whenever
    /// it survives means a refresh never re-files a book (a path change) just because the
    /// source's series arrived in a different order.
    /// </summary>
    public static Entry? ChoosePrimary(IReadOnlyList<Entry> current, IReadOnlyList<Entry> source, string? explicitPrimary = null)
    {
        if (source.Count == 0)
        {
            return null;
        }

        Entry? Find(string? name) =>
            string.IsNullOrWhiteSpace(name)
                ? null
                : source.FirstOrDefault(s => string.Equals(s.Name, name.Trim(), StringComparison.Ordinal))
                    ?? source.FirstOrDefault(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        var chosen = Find(explicitPrimary);
        if (chosen is not null)
        {
            return chosen;
        }

        foreach (var have in current)
        {
            chosen = source.FirstOrDefault(s => string.Equals(s.Name, have.Name, StringComparison.Ordinal));
            if (chosen is not null)
            {
                return chosen;
            }
        }

        return source[0];
    }

    /// <summary>The set a book would have after taking <paramref name="source"/>, with the primary chosen.</summary>
    public static Resolved Resolve(IReadOnlyList<Entry> current, IEnumerable<Entry>? source, string? explicitPrimary = null)
    {
        var canonical = Canonicalize(source);
        var primary = ChoosePrimary(current, canonical, explicitPrimary);
        return new Resolved(primary, canonical.Where(e => !ReferenceEquals(e, primary)).ToList());
    }

    /// <summary>
    /// Whether the two sets differ in any way a user cares about: a series added or removed, a
    /// different part in a series both have, or a different primary. Names compare exactly (a
    /// casing correction is a real change); parts compare by <see cref="SeriesPartEquivalence"/>,
    /// so "2" and "2.0" do not.
    /// </summary>
    public static bool Differs(Resolved current, Resolved proposed)
    {
        var currentAll = current.All.ToList();
        var proposedAll = proposed.All.ToList();
        if (currentAll.Count != proposedAll.Count)
        {
            return true;
        }

        if (!string.Equals(current.Primary?.Name, proposed.Primary?.Name, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var have in currentAll)
        {
            var match = proposedAll.FirstOrDefault(p => string.Equals(p.Name, have.Name, StringComparison.Ordinal));
            if (match is null || !SamePart(have.Part, match.Part))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SamePart(string? a, string? b)
    {
        var blankA = string.IsNullOrWhiteSpace(a);
        var blankB = string.IsNullOrWhiteSpace(b);
        return blankA || blankB
            ? blankA && blankB
            : SeriesPartEquivalence.PartsEquivalentClr(a, b) || string.Equals(a!.Trim(), b!.Trim(), StringComparison.Ordinal);
    }

    /// <summary>The set a stored book has, primary first - from its relations when loaded, else its mirrored columns.</summary>
    public static Resolved OfBook(Database.Models.Audiobook book)
    {
        var rows = book.SeriesRelations;
        if (rows is { Count: > 0 })
        {
            var ordered = rows.OrderByDescending(r => r.IsPrimary).ThenBy(r => r.SortOrder).ToList();
            var primary = ordered.FirstOrDefault(r => r.IsPrimary);
            return new Resolved(
                primary is null ? null : new Entry(primary.SeriesName, primary.SeriesPart),
                ordered.Where(r => !r.IsPrimary).Select(r => new Entry(r.SeriesName, r.SeriesPart)).ToList());
        }

        return string.IsNullOrWhiteSpace(book.Series)
            ? new Resolved(null, new List<Entry>())
            : new Resolved(new Entry(book.Series.Trim(), book.SeriesPart), new List<Entry>());
    }

    /// <summary>The set a domain book has, primary first.</summary>
    public static Resolved OfBook(Audiobook book) =>
        new(
            string.IsNullOrWhiteSpace(book.Series) ? null : new Entry(book.Series.Trim(), book.SeriesPart),
            (book.AdditionalSeries ?? new List<SeriesRelation>()).Select(r => new Entry(r.Name, r.Part)).ToList());

    /// <summary>
    /// The set as one display string, primary marked when there is more than one series
    /// (<c>"Main #1 (primary); Spinoff #3"</c>) - what a diff shows on each side. Null when empty.
    /// </summary>
    public static string? Format(Resolved set)
    {
        var all = set.All.ToList();
        if (all.Count == 0)
        {
            return null;
        }

        return string.Join("; ", all.Select(e =>
        {
            var text = string.IsNullOrWhiteSpace(e.Part) ? e.Name : $"{e.Name} #{e.Part!.Trim()}";
            return all.Count > 1 && ReferenceEquals(e, set.Primary) ? $"{text} (primary)" : text;
        }));
    }

    /// <summary>Writes <paramref name="set"/> onto a domain book: the primary, and the rest as additional series.</summary>
    public static void ApplyTo(Audiobook book, Resolved set)
    {
        book.Series = set.Primary?.Name;
        book.SeriesPart = set.Primary?.Part;
        book.AdditionalSeries = set.Additional.Select(e => new SeriesRelation(e.Name, e.Part)).ToList();
    }
}
