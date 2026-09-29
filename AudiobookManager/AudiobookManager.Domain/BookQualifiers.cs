namespace AudiobookManager.Domain;

/// <summary>
/// One qualifier a book can carry - a fact about how the work was produced (abridged,
/// dramatized, ...) that makes it a different listening experience from the plain edition.
/// <see cref="Key"/> is the stable identifier stored in the database; <see cref="Label"/> is the
/// display name and the text inside the parenthetical suffix, so renaming a label later is a
/// rewrite of files (which the consistency check surfaces), not of stored data.
/// </summary>
public sealed record BookQualifier(string Key, string Label)
{
    /// <summary>The text appended to a book name / series name, e.g. <c>" (Dramatized)"</c>.</summary>
    public string Suffix => $" ({Label})";
}

/// <summary>
/// The canonical set of book qualifiers, and the rules for turning a book's clean name and its
/// qualifiers into the name that is written to disk and back.
///
/// <b>Stored vs written.</b> The database (and <see cref="Audiobook.BookName"/> /
/// <see cref="Audiobook.Series"/>) hold the clean values, and the qualifiers live beside them.
/// Everything that lands on disk - the library path, the m4b tags, <c>metadata.opf</c> - uses the
/// effective names built by <see cref="Apply"/>: <c>Killing Floor</c> + dramatized becomes
/// <c>Killing Floor (Dramatized)</c>. Keeping the clean values in the database is what lets series
/// grouping, the series roster, similar-value detection and the metadata refresh diff work on the
/// real title without knowing about qualifiers.
///
/// <b>Reading back.</b> A file's tags carry the suffix, but nothing in them says whether it is a
/// qualifier or simply part of the title, so <see cref="ApplyExpected"/> only strips a suffix the
/// book's stored qualifiers say belongs there. A book with no stored qualifiers is never split, so
/// a title that merely ends in <c>(Abridged)</c> is left exactly as it was written.
///
/// This list is deliberately fixed in code rather than configurable: it is exposed to the client
/// over <c>GET /api/settings/book-qualifiers</c>, so the frontend has no list of its own to drift
/// from. Adding a qualifier is one entry in <see cref="All"/>.
/// </summary>
public static class BookQualifiers
{
    /// <summary>Every supported qualifier, in the order it should be offered to a user.</summary>
    public static readonly IReadOnlyList<BookQualifier> All = new List<BookQualifier>
    {
        new("abridged", "Abridged"),
        new("dramatized", "Dramatized"),
    };

    private const string ListSeparator = ", ";

    public static BookQualifier? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var trimmed = key.Trim();
        return All.FirstOrDefault(q => string.Equals(q.Key, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static BookQualifier? FindByLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var trimmed = label.Trim();
        return All.FirstOrDefault(q => string.Equals(q.Label, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The canonical form of a set of qualifier keys: trimmed, lowercased, de-duplicated, in
    /// alphabetical label order (which is the order the suffixes are written in), and limited to
    /// keys the registry knows. A key the registry does not know has no label, so it can never be
    /// written into a name and read back - keeping it would make the book permanently mismatched
    /// and un-saveable the moment a qualifier is retired. Dropping it here means the next save of
    /// such a book simply removes it.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string?>? keys)
    {
        var known = new List<BookQualifier>();

        foreach (var raw in keys ?? Enumerable.Empty<string?>())
        {
            var qualifier = Find(raw);
            if (qualifier is not null && !known.Contains(qualifier))
            {
                known.Add(qualifier);
            }
        }

        return known
            .OrderBy(q => q.Label, StringComparer.OrdinalIgnoreCase)
            .Select(q => q.Key)
            .ToList();
    }

    private static List<BookQualifier> KnownInOrder(IEnumerable<string?>? keys) =>
        Normalize(keys).Select(k => Find(k)!).ToList();

    /// <summary>
    /// The name as it is written to disk: the clean <paramref name="name"/> followed by one
    /// <c>" (Label)"</c> per qualifier, alphabetically. A blank name is returned unchanged - there
    /// is nothing to qualify.
    /// </summary>
    public static string? Apply(string? name, IEnumerable<string?>? qualifierKeys)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var suffix = string.Concat(KnownInOrder(qualifierKeys).Select(q => q.Suffix));
        return suffix.Length == 0 ? name : name + suffix;
    }

    /// <summary>
    /// Reshapes a book parsed from a file into the clean-name-plus-qualifiers form the database
    /// uses, but only for the qualifiers <paramref name="expectedKeys"/> (the stored ones) name.
    ///
    /// The suffixes must be exactly what <see cref="Apply"/> would have written: the book name
    /// ends with them, the series (when the file has one) ends with the same set, and they appear
    /// in canonical order. Anything short of that leaves the book untouched, so the mismatch
    /// surfaces as a real difference - a file whose series lost its suffix, or whose suffixes are
    /// out of order, does not read as consistent.
    /// </summary>
    public static void ApplyExpected(Audiobook parsed, IEnumerable<string?>? expectedKeys)
    {
        var expected = KnownInOrder(expectedKeys);
        if (expected.Count == 0)
        {
            return;
        }

        var (name, nameStripped) = StripSuffixes(parsed.BookName, expected);
        if (nameStripped.Count == 0)
        {
            return;
        }

        var series = parsed.Series;
        if (!string.IsNullOrEmpty(series))
        {
            var (strippedSeries, seriesStripped) = StripSuffixes(series, expected);
            if (!seriesStripped.SequenceEqual(nameStripped, StringComparer.Ordinal))
            {
                return;
            }

            series = strippedSeries;
        }

        parsed.BookName = name;
        parsed.Series = series;
        parsed.Qualifiers = nameStripped;
    }

    /// <summary>
    /// Peels the expected qualifiers' suffixes off the end of <paramref name="value"/>, last
    /// (alphabetically greatest) first, stopping at the first one that is not there. Never strips
    /// the whole value away: a name that is only a suffix keeps it.
    /// </summary>
    private static (string? Value, List<string> Stripped) StripSuffixes(string? value, IReadOnlyList<BookQualifier> expected)
    {
        var stripped = new List<string>();
        if (string.IsNullOrEmpty(value))
        {
            return (value, stripped);
        }

        var current = value;
        for (var i = expected.Count - 1; i >= 0; i--)
        {
            var suffix = expected[i].Suffix;
            if (current.Length > suffix.Length
                && current.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(current[..^suffix.Length]))
            {
                current = current[..^suffix.Length];
                stripped.Insert(0, expected[i].Key);
            }
            else
            {
                break;
            }
        }

        return (current, stripped);
    }

    /// <summary>
    /// Serialized display form of a set of qualifiers (<c>"Abridged, Dramatized"</c>), shared by
    /// the tag consistency checker and the selective tag-mismatch resolution so the value shown to
    /// the user and the value parsed back cannot drift.
    /// </summary>
    public static string Format(IEnumerable<string?>? keys) =>
        string.Join(ListSeparator, KnownInOrder(keys).Select(q => q.Label));

    /// <summary>The inverse of <see cref="Format"/>.</summary>
    public static List<string> Parse(string? serialized) =>
        Normalize((serialized ?? string.Empty)
            .Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(label => FindByLabel(label)?.Key));
}
