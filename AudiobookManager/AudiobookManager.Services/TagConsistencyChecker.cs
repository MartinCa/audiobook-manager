using AudiobookManager.Domain;
using AudiobookManager.FileManager;

namespace AudiobookManager.Services;

/// <summary>
/// Compares the metadata a caller intended to persist against what a fresh tag parse actually
/// read back, field by field. Shared by <see cref="LibraryConsistencyService"/> (comparing
/// library metadata against the m4b on disk) and <see cref="AudiobookService"/> (verifying a
/// just-written save actually round-tripped before the DB record is updated).
/// </summary>
public static class TagConsistencyChecker
{
    public static List<(string Field, string Expected, string Actual)> FindMismatches(Audiobook expected, Audiobook actual)
    {
        var mismatches = new List<(string Field, string Expected, string Actual)>();

        void Compare(string field, string? expectedValue, string? actualValue)
        {
            if (!string.Equals(expectedValue ?? "", actualValue ?? "", StringComparison.Ordinal))
            {
                mismatches.Add((field, expectedValue ?? "", actualValue ?? ""));
            }
        }

        Compare("Author", FormatPersons(expected.Authors), FormatPersons(actual.Authors));
        Compare("Narrators", FormatPersons(expected.Narrators), FormatPersons(actual.Narrators));
        Compare("Book Name", expected.BookName, actual.BookName);
        Compare("Subtitle", expected.Subtitle, actual.Subtitle);
        Compare("Series", expected.Series, actual.Series);
        Compare("Series Part", expected.SeriesPart, actual.SeriesPart);
        Compare("Year", expected.Year?.ToString(), actual.Year?.ToString());
        Compare("Description", expected.Description, actual.Description);
        Compare("Copyright", expected.Copyright, actual.Copyright);
        Compare("Publisher", expected.Publisher, actual.Publisher);
        Compare("Language", expected.Language, actual.Language);
        Compare("Rating", expected.Rating, actual.Rating);
        Compare("Asin", expected.Asin, actual.Asin);
        Compare("Www", expected.Www, actual.Www);
        Compare("Genres", FormatGenres(expected.Genres), FormatGenres(actual.Genres));

        return mismatches;
    }

    // Both formatters must normalize the same way the tag writer does, or the round-trip
    // verification in AudiobookService reports a mismatch that no amount of re-saving can clear.
    // AudiobookTagHandler.GetStringFromListOfPersons de-duplicates names before writing them, and
    // an empty genre string round-trips as no genres at all - so a repeated author or a blank
    // genre entry must not be treated as a difference here.
    //
    // They are `internal` (same assembly) so the selective tag-mismatch resolution can reuse the
    // exact same normalization to parse a chosen serialized value back into a domain collection
    // (see ParseGenres/ParsePersons) - otherwise two unconnected copies would be free to drift,
    // and a resolve would write tags the checker then claims still mismatch.
    internal static string FormatGenres(IEnumerable<string> genres) =>
        string.Join(", ", genres
            .Select(g => g?.Trim() ?? "")
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(g => g, StringComparer.Ordinal));

    internal static string FormatPersons(IEnumerable<Person> persons) =>
        string.Join(", ", persons
            .Select(p => p.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal));

    /// <summary>
    /// Inverse of <see cref="FormatGenres"/>: splits a serialized genre list back into the genre
    /// strings the same delimiter produced. Only used on values that came from
    /// <see cref="FindMismatches"/> (library or file side); raw file tags are still parsed with
    /// <see cref="AudiobookTagHandler.ParseGenresFromString"/>, whose '/' delimiter is the m4b
    /// genre-separator, not this display form.
    /// </summary>
    internal static List<string> ParseGenres(string? serializedGenres) =>
        (serializedGenres ?? string.Empty)
            .Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    /// <summary>The inverse of <see cref="FormatPersons"/>; comma-joined names split back out.</summary>
    internal static List<Person> ParsePersons(string? serializedPersons) =>
        AudiobookTagHandler.ParsePersonsFromString(serializedPersons);
}
