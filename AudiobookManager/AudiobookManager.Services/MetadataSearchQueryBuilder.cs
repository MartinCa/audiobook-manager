using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// The single fallback/priority rule for the default query used to search online metadata
/// sources. Every backend flow that seeds a metadata search must build its query through this
/// method rather than re-deriving its own precedence, so <see cref="PendingOnlineMatchService"/>
/// (the bulk background search) and <c>POST api/metadata-search/default-query</c> (which seeds the
/// interactive search dialog) never drift. The frontend's <c>buildDefaultMetadataSearchQuery</c> is
/// only the as-stored fallback used when that request fails.
///
/// Priority, highest to lowest, falling back to the next when the higher one is blank:
/// 1. "Author - Book name" (authors joined with ", ", matching the display convention used
///    elsewhere, e.g. <c>authors.join(", ")</c> on the frontend)
/// 2. Book name
/// 3. File name
///
/// A book's main author, when it has one, is the only author in the query: a co-author or an
/// editor in the "Author - Book name" text is as likely to hide the book as to find it.
///
/// Author names are first written per the <see cref="SearchInitialsHandling"/> that applies to the
/// source the query is for (a source may index "George R.R. Martin" and miss "George R. R. Martin"),
/// so one book can have a different default query per source - see <see cref="BuildPerSource"/>.
/// The book name and file name are never touched.
/// </summary>
public static class MetadataSearchQueryBuilder
{
    public static string Build(
        IEnumerable<string>? authorNames,
        string? bookName,
        string? fileName,
        SearchInitialsHandling initialsHandling = SearchInitialsHandling.AsStored,
        string? mainAuthor = null)
    {
        var trimmedBookName = bookName?.Trim() ?? string.Empty;
        // The main author stands in for the whole list. The caller is responsible for it being one
        // of the book's authors (Audiobook.EffectiveMainAuthor / the DTO mapping guarantee that).
        var trimmedMain = mainAuthor?.Trim();
        var effectiveAuthors = string.IsNullOrEmpty(trimmedMain)
            ? authorNames ?? Enumerable.Empty<string>()
            : new[] { trimmedMain };
        var trimmedAuthors = effectiveAuthors
            .Select(a => FormatInitials(a.Trim(), initialsHandling))
            .Where(a => a.Length > 0)
            .ToList();

        if (trimmedAuthors.Count > 0 && trimmedBookName.Length > 0)
        {
            return $"{string.Join(", ", trimmedAuthors)} - {trimmedBookName}";
        }

        if (trimmedBookName.Length > 0)
        {
            return trimmedBookName;
        }

        return fileName?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// The default query for each of <paramref name="sourceNames"/>, each written with the initials
    /// handling that applies to that source (<see cref="LibrarySettings.SearchInitialsHandlingFor"/>:
    /// its own override, else the library-wide setting). Keyed ignoring case.
    /// </summary>
    public static Dictionary<string, string> BuildPerSource(
        IEnumerable<string> sourceNames,
        IEnumerable<string>? authorNames,
        string? bookName,
        string? fileName,
        LibrarySettings settings,
        string? mainAuthor = null)
    {
        var authors = authorNames?.ToList();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sourceNames)
        {
            result.TryAdd(source, Build(
                authors, bookName, fileName, settings.SearchInitialsHandlingFor(source), mainAuthor));
        }

        return result;
    }

    /// <summary>
    /// Re-spells an author name's initials run for the search query. Always dotted: the point is
    /// the form sources index, not the library's own punctuation preference.
    /// </summary>
    public static string FormatInitials(string authorName, SearchInitialsHandling handling) => handling switch
    {
        SearchInitialsHandling.AsStored => authorName,
        SearchInitialsHandling.Compact =>
            InitialsSpacingFormatter.Format(authorName, InitialsSpacing.Unspaced, InitialsPunctuation.Dotted),
        SearchInitialsHandling.Spaced =>
            InitialsSpacingFormatter.Format(authorName, InitialsSpacing.Spaced, InitialsPunctuation.Dotted),
        _ => throw new ArgumentOutOfRangeException(nameof(handling), handling, "Unknown search initials handling"),
    };
}
