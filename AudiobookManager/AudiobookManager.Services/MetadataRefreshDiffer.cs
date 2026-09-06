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

        void Add(string field, string? libraryValue, string? sourceValue)
        {
            if (!string.Equals(Trim(libraryValue), Trim(sourceValue), StringComparison.Ordinal))
            {
                diffs.Add(new MetadataRefreshDiff(field, Trim(libraryValue), Trim(sourceValue)));
            }
        }

        Add("Authors", JoinNames(book.Authors.Select(a => a.Name)), JoinNames(fetched.Authors.Select(a => a.Name)));
        Add("Narrators", JoinNames(book.Narrators.Select(n => n.Name)), JoinNames(fetched.Narrators.Select(n => n.Name)));
        Add("BookName", book.BookName, fetched.BookName);
        Add("Subtitle", book.Subtitle, fetched.Subtitle);
        Add("Series", book.Series, fetched.Series?.FirstOrDefault()?.SeriesName);
        Add("SeriesPart", book.SeriesPart, fetched.Series?.FirstOrDefault()?.SeriesPart);

        // Year is non-nullable on the DB model; a source that reports no year must not offer
        // blanking it, so a null source year never becomes a diff.
        if (fetched.Year.HasValue)
        {
            Add("Year", book.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                fetched.Year.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Add("Genres", JoinList(book.Genres.Select(g => g.Name)), JoinList(fetched.Genres));
        Add("Description", book.Description, fetched.Description);

        // Language: both sides fold through the managed alias table, so "English" (source) does
        // not differ from "en" (stored) - and an unrecognizable source value is left alone rather
        // than offering a diff to an unmanaged code.
        Add("Language", Languages.Normalize(book.Language) ?? book.Language, Languages.Normalize(fetched.Language) ?? Languages.Normalize(book.Language));

        Add("Rating", book.Rating, fetched.Rating?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add("Copyright", book.Copyright, fetched.Copyright);
        Add("Publisher", book.Publisher, fetched.Publisher);
        Add("Asin", book.Asin, fetched.Asin);

        return diffs;
    }

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