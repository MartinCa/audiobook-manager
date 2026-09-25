using System.Text.RegularExpressions;
using AudiobookManager.Database.Models;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Services;

/// <summary>
/// Computes which fields a fetched <see cref="MetadataSearchResult"/> would change on a stored
/// book, using the same normalizations the rest of the pipeline applies, so formatting-only
/// differences do not surface as pending changes.
///
/// Structural fields (authors, book name, series, year) are diffed for display, but applying
/// them later still goes through the normal save pipeline (the binding invariant) - the diff
/// here is what the approval UI shows, not a write path.
/// </summary>
public static class MetadataRefreshDiffer
{
    private const string ListSeparator = " / ";

    public static IEnumerable<MetadataRefreshDiff> Diff(
        Database.Models.Audiobook book, MetadataSearchResult fetched,
        Domain.InitialsSpacing spacing, Domain.InitialsPunctuation punctuation)
    {
        var diffs = new List<MetadataRefreshDiff>();
        var add = MakeAdd(diffs);

        add(MetadataRefreshFields.Authors,
            JoinNames(book.Authors.Select(a => a.Name), spacing, punctuation),
            JoinNames(fetched.Authors.Select(a => a.Name), spacing, punctuation));
        add(MetadataRefreshFields.Narrators,
            JoinNames(book.Narrators.Select(n => n.Name), spacing, punctuation),
            JoinNames(fetched.Narrators.Select(n => n.Name), spacing, punctuation));
        add(MetadataRefreshFields.BookName, book.BookName, fetched.BookName);
        add(MetadataRefreshFields.Subtitle, book.Subtitle, fetched.Subtitle);
        add(MetadataRefreshFields.Series, book.Series, fetched.Series?.FirstOrDefault()?.SeriesName);
        add(MetadataRefreshFields.SeriesPart, book.SeriesPart, fetched.Series?.FirstOrDefault()?.SeriesPart);

        // Year is non-nullable on the DB model; a source that reports no year must not offer
        // blanking it, so a null source year never becomes a diff.
        if (fetched.Year.HasValue)
        {
            add(MetadataRefreshFields.Year, book.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                fetched.Year.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        add(MetadataRefreshFields.Genres, JoinList(book.Genres.Select(g => g.Name)), JoinList(fetched.Genres));
        add(MetadataRefreshFields.Description, book.Description, fetched.Description);

        // Language: both sides fold through the managed alias table, so "English" (source) does
        // not differ from "en" (stored) - and an unrecognizable source value is left alone rather
        // than offering a diff to an unmanaged code. The fallbacks must be symmetric: when the
        // stored value is ALSO unrecognized (e.g. backfilled "spa" from an m4b tag), both sides
        // normalize to null, and the source fallback has to land on the stored value too -
        // otherwise a nothing-changed pair reads as "spa → (blank)".
        add(MetadataRefreshFields.Language, Languages.Normalize(book.Language) ?? book.Language,
            Languages.Normalize(fetched.Language) ?? (Languages.Normalize(book.Language) ?? book.Language));

        add(MetadataRefreshFields.Rating, book.Rating, fetched.Rating?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        add(MetadataRefreshFields.Copyright, book.Copyright, fetched.Copyright);
        add(MetadataRefreshFields.Publisher, book.Publisher, fetched.Publisher);
        add(MetadataRefreshFields.Asin, book.Asin, fetched.Asin);

        return diffs;
    }

    /// <summary>
    /// The same comparison <see cref="Diff"/> runs, against an already-stored
    /// <see cref="PendingRefreshPayload.Snapshot"/> instead of a freshly-scraped
    /// <see cref="MetadataSearchResult"/> - used to recompute (and self-heal) the changed-field
    /// list for a pending row written before <c>ChangedFieldsJson</c> existed. Deliberately not
    /// implemented by adapting the snapshot into a <see cref="MetadataSearchResult"/>: the two
    /// carry different shapes (a snapshot's authors/narrators/genres are already plain strings,
    /// not <see cref="Person"/>/richer objects), so sharing <see cref="Diff"/> would cost more in
    /// adapter code than the handful of comparisons this repeats.
    /// </summary>
    public static IEnumerable<MetadataRefreshDiff> DiffSnapshot(
        Database.Models.Audiobook book, PendingRefreshPayload.Snapshot snapshot,
        Domain.InitialsSpacing spacing, Domain.InitialsPunctuation punctuation)
    {
        var diffs = new List<MetadataRefreshDiff>();
        var add = MakeAdd(diffs);

        add(MetadataRefreshFields.Authors,
            JoinNames(book.Authors.Select(a => a.Name), spacing, punctuation),
            JoinNames(snapshot.Authors, spacing, punctuation));
        add(MetadataRefreshFields.Narrators,
            JoinNames(book.Narrators.Select(n => n.Name), spacing, punctuation),
            JoinNames(snapshot.Narrators, spacing, punctuation));
        add(MetadataRefreshFields.BookName, book.BookName, snapshot.BookName);
        add(MetadataRefreshFields.Subtitle, book.Subtitle, snapshot.Subtitle);
        add(MetadataRefreshFields.Series, book.Series, snapshot.SeriesName);
        add(MetadataRefreshFields.SeriesPart, book.SeriesPart, snapshot.SeriesPart);

        if (snapshot.Year.HasValue)
        {
            add(MetadataRefreshFields.Year, book.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                snapshot.Year.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        add(MetadataRefreshFields.Genres, JoinList(book.Genres.Select(g => g.Name)), JoinList(snapshot.Genres));
        add(MetadataRefreshFields.Description, book.Description, snapshot.Description);

        add(MetadataRefreshFields.Language, Languages.Normalize(book.Language) ?? book.Language,
            Languages.Normalize(snapshot.Language) ?? (Languages.Normalize(book.Language) ?? book.Language));

        add(MetadataRefreshFields.Rating, book.Rating, snapshot.Rating);
        add(MetadataRefreshFields.Copyright, book.Copyright, snapshot.Copyright);
        add(MetadataRefreshFields.Publisher, book.Publisher, snapshot.Publisher);
        add(MetadataRefreshFields.Asin, book.Asin, snapshot.Asin);

        return diffs;
    }

    private static Action<string, string?, string?> MakeAdd(List<MetadataRefreshDiff> diffs) =>
        (field, libraryValue, sourceValue) =>
        {
            var trimmedLibrary = Trim(libraryValue);
            var trimmedSource = Trim(sourceValue);
            if (!string.Equals(ComparableValue(field, trimmedLibrary), ComparableValue(field, trimmedSource), StringComparison.Ordinal))
            {
                diffs.Add(new MetadataRefreshDiff(field, trimmedLibrary, trimmedSource));
            }
        };

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A standalone single-letter token, dotted or not ("R." or "R"), surrounded by spaces or a
    /// list boundary - matches a lone middle initial <see cref="InitialsSpacingFormatter"/>
    /// deliberately leaves untouched. That formatter only promotes a bare (undotted) single
    /// letter to an initial when it sits next to a dotted initial or another bare one (a run of
    /// 2+) - a single stray letter like the "R" in "Andrew R Chow" has no such neighbor, so
    /// Format() cannot tell it apart from a genuine one-letter English word and, correctly for a
    /// value it might WRITE back to the library, leaves it alone. That conservatism is wrong for
    /// comparison, though: this field is never anything but a person's name, so an isolated
    /// capital letter is always a middle initial here, dot or no dot. Stripping the dot (if any)
    /// from every standalone letter token - independent of whatever InitialsSpacingFormatter
    /// already normalized - makes "Andrew R. Chow" and "Andrew R Chow" compare equal without
    /// changing what JoinNames actually displays or writes.
    /// </summary>
    private static readonly Regex LoneInitialRegex = new(@"(?<=^|[\s/])([A-Za-z])\.(?=[\s/]|$)", RegexOptions.Compiled);

    private static string? ComparableValue(string field, string? value)
    {
        if (value is null)
        {
            return null;
        }

        return field is MetadataRefreshFields.Authors or MetadataRefreshFields.Narrators
            ? LoneInitialRegex.Replace(value, "$1")
            : value;
    }

    /// <summary>
    /// Names are formatted to the library's configured <see cref="InitialsSpacing"/>/
    /// <see cref="InitialsPunctuation"/> convention BEFORE joining/deduping/sorting - not just a
    /// spacing fold. "Andrew R. Chow" (library, Dotted) vs "Andrew R Chow" (a source that omits
    /// the period) is a punctuation difference, not merely a spacing one, and a source's
    /// convention need not match the library's in either respect. Formatting both sides to the
    /// SAME canonical form is what <see cref="Consistency.Detectors.InitialsSpacingIssueDetector"/>
    /// already validates stored names against, so this reuses it rather than inventing a second,
    /// narrower normalization - the diff's displayed LibraryValue/SourceValue therefore also show
    /// the canonical form (consistent with how Genres already displays a sorted/deduped form
    /// rather than the literal scraped order).
    /// </summary>
    private static string? JoinNames(IEnumerable<string?> names, Domain.InitialsSpacing spacing, Domain.InitialsPunctuation punctuation) =>
        JoinList(names.Select(n => string.IsNullOrWhiteSpace(n) ? n : InitialsSpacingFormatter.Format(n, spacing, punctuation)));

    private static string? JoinList(IEnumerable<string?> values)
    {
        var meaningful = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.Ordinal)
            // Order is presentation, not data: the same names in a different order are not a
            // change a refresh should offer. Sorting the joined form makes the comparison
            // order-insensitive while the rendered diff keeps a deterministic display order.
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return meaningful.Count == 0 ? null : string.Join(ListSeparator, meaningful);
    }
}