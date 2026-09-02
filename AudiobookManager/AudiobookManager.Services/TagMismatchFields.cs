using AudiobookManager.Domain;

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
/// and the domain <see cref="Audiobook"/> properties. Used by the resolve side of selective
/// resolution: applying the user's chosen values back onto the domain object that
/// <see cref="AudiobookService.UpdateAudiobook"/> persists. The read side reads straight from
/// <see cref="TagConsistencyChecker.FindMismatches"/>; this class holds no formatter of its own,
/// so the serialization shared with the checker cannot drift.
/// </summary>
public static class TagMismatchFields
{
    /// <summary>
    /// Fields that drive the library path (<c>AudiobookFileHandler.GenerateRelativeAudiobookPath</c>):
    /// clearing one relocates the file to a mangled path (e.g. <c>"/ - BookName/ - BookName.m4b"</c>)
    /// and/or leaves the DB holding the old value. The resolve endpoint rejects a null/empty choice
    /// for these, and the UI renders no "Keep Neither" option for them.
    /// </summary>
    public static readonly HashSet<string> StructuralFields = new(StringComparer.Ordinal)
    {
        "Author", "Book Name", "Year"
    };

    /// <summary>
    /// Applies a chosen serialized value for a field onto a domain audiobook. A null/empty value
    /// clears the field - except for <see cref="StructuralFields"/>, which must always keep a
    /// value (an empty choice there is a caller error). Only the fields whose serialized form the
    /// user changed need applying.
    /// </summary>
    public static void ApplyValue(Audiobook audiobook, string field, string? value)
    {
        if (StructuralFields.Contains(field) && string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"Field '{field}' cannot be cleared: it determines the library path");
        }

        switch (field)
        {
            case "Author":
                audiobook.Authors = TagConsistencyChecker.ParsePersons(value);
                break;
            case "Narrators":
                audiobook.Narrators = TagConsistencyChecker.ParsePersons(value);
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
                audiobook.Genres = TagConsistencyChecker.ParseGenres(value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown tag field");
        }
    }
}
