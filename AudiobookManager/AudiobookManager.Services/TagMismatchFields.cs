using AudiobookManager.Domain;
using AudiobookManager.FileManager;

namespace AudiobookManager.Services;

/// <summary>
/// One differing tag field exposed to the selective tag-mismatch resolution screen, with the
/// serialized library value and file value for the field. Field names must be stable and match
/// the keys <see cref="TagConsistencyChecker"/> reports, so the resolve path can map a chosen
/// value back onto the domain <see cref="Audiobook"/>.
/// </summary>
public record TagMismatchField(string Field, string? LibraryValue, string? FileValue);

/// <summary>
/// Maps the tag fields <see cref="TagConsistencyChecker"/> compares between serialized values
/// and the domain <see cref="Audiobook"/> properties. Used by both sides of selective resolution:
/// reading a mismatch's candidates and applying the user's chosen values back onto the domain
/// object that <see cref="AudiobookService.UpdateAudiobook"/> persists.
/// </summary>
public static class TagMismatchFields
{
    public static readonly string[] AllFields =
    {
        "Author", "Narrators", "Book Name", "Subtitle", "Series", "Series Part", "Year",
        "Description", "Copyright", "Publisher", "Language", "Rating", "Asin", "Www", "Genres"
    };

    /// <summary>Reads the serialized value for a field from a domain audiobook.</summary>
    public static string? GetValue(Audiobook audiobook, string field)
    {
        return field switch
        {
            "Author" => FormatPersons(audiobook.Authors),
            "Narrators" => FormatPersons(audiobook.Narrators),
            "Book Name" => audiobook.BookName,
            "Subtitle" => audiobook.Subtitle,
            "Series" => audiobook.Series,
            "Series Part" => audiobook.SeriesPart,
            "Year" => audiobook.Year?.ToString(),
            "Description" => audiobook.Description,
            "Copyright" => audiobook.Copyright,
            "Publisher" => audiobook.Publisher,
            "Language" => audiobook.Language,
            "Rating" => audiobook.Rating,
            "Asin" => audiobook.Asin,
            "Www" => audiobook.Www,
            "Genres" => FormatGenres(audiobook.Genres),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown tag field")
        };
    }

    /// <summary>
    /// Applies a chosen serialized value for a field onto a domain audiobook. A null/empty value
    /// clears the field. Only the fields whose serialized form the user changed need applying.
    /// </summary>
    public static void ApplyValue(Audiobook audiobook, string field, string? value)
    {
        switch (field)
        {
            case "Author":
                audiobook.Authors = ParsePersons(value);
                break;
            case "Narrators":
                audiobook.Narrators = ParsePersons(value);
                break;
            case "Book Name":
                audiobook.BookName = string.IsNullOrEmpty(value) ? null : value;
                break;
            case "Subtitle":
                audiobook.Subtitle = string.IsNullOrEmpty(value) ? null : value;
                break;
            case "Series":
                audiobook.Series = string.IsNullOrEmpty(value) ? null : value;
                break;
            case "Series Part":
                audiobook.SeriesPart = string.IsNullOrEmpty(value) ? null : value;
                break;
            case "Year":
                audiobook.Year = int.TryParse(value, out var year) ? year : null;
                break;
            case "Description":
                audiobook.Description = string.IsNullOrEmpty(value) ? null : value;
                break;
            case "Copyright":
                audiobook.Copyright = string.IsNullOrEmpty(value) ? null : value;
                break;
            case "Publisher":
                audiobook.Publisher = string.IsNullOrEmpty(value) ? null : value;
                break;
            case "Language":
                audiobook.Language = string.IsNullOrEmpty(value) ? null : value;
                break;
            case "Rating":
                audiobook.Rating = string.IsNullOrEmpty(value) ? null : value;
                break;
            case "Asin":
                audiobook.Asin = string.IsNullOrEmpty(value) ? null : value;
                break;
            case "Www":
                audiobook.Www = string.IsNullOrEmpty(value) ? null : value;
                break;
            case "Genres":
                audiobook.Genres = ParseGenres(value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown tag field");
        }
    }

    private static string FormatGenres(IEnumerable<string> genres) =>
        string.Join(", ", genres
            .Select(g => g?.Trim() ?? "")
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(g => g, StringComparer.Ordinal));

    private static string FormatPersons(IEnumerable<Person> persons) =>
        string.Join(", ", persons
            .Select(p => p.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal));

    private static List<Person> ParsePersons(string? value) =>
        AudiobookTagHandler.ParsePersonsFromString(value);

    private static List<string> ParseGenres(string? value) =>
        AudiobookTagHandler.ParseGenresFromString(value);
}
