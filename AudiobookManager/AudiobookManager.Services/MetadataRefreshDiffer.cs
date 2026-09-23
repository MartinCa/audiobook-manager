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

    public static IEnumerable<MetadataRefreshDiff> Diff(Database.Models.Audiobook book, MetadataSearchResult fetched)
    {
        var diffs = new List<MetadataRefreshDiff>();
        var add = MakeAdd(diffs);

        add(MetadataRefreshFields.Authors, JoinNames(book.Authors.Select(a => a.Name)), JoinNames(fetched.Authors.Select(a => a.Name)));
        add(MetadataRefreshFields.Narrators, JoinNames(book.Narrators.Select(n => n.Name)), JoinNames(fetched.Narrators.Select(n => n.Name)));
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
    public static IEnumerable<MetadataRefreshDiff> DiffSnapshot(Database.Models.Audiobook book, PendingRefreshPayload.Snapshot snapshot)
    {
        var diffs = new List<MetadataRefreshDiff>();
        var add = MakeAdd(diffs);

        add(MetadataRefreshFields.Authors, JoinNames(book.Authors.Select(a => a.Name)), JoinList(snapshot.Authors));
        add(MetadataRefreshFields.Narrators, JoinNames(book.Narrators.Select(n => n.Name)), JoinList(snapshot.Narrators));
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
            if (!string.Equals(Trim(libraryValue), Trim(sourceValue), StringComparison.Ordinal))
            {
                diffs.Add(new MetadataRefreshDiff(field, Trim(libraryValue), Trim(sourceValue)));
            }
        };

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? JoinNames(IEnumerable<string?> names) =>
        JoinList(names);

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