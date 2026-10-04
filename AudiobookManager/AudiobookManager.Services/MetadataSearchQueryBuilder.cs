using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// The single fallback/priority rule for the default query used to search online metadata
/// sources. Every backend flow that seeds a metadata search must build its query through this
/// method rather than re-deriving its own precedence, so <see cref="PendingOnlineMatchService"/>
/// (the bulk background search) never drifts from the frontend's mirrored
/// <c>buildDefaultMetadataSearchQuery</c> helper (used to seed the interactive search dialog).
///
/// Priority, highest to lowest, falling back to the next when the higher one is blank:
/// 1. "Author - Book name" (authors joined with ", ", matching the display convention used
///    elsewhere, e.g. <c>authors.join(", ")</c> on the frontend)
/// 2. Book name
/// 3. File name
///
/// Author names are first written per the library's <see cref="SearchInitialsHandling"/> setting
/// (a source may index "George R.R. Martin" and miss "George R. R. Martin"). The book name and
/// file name are never touched.
/// </summary>
public static class MetadataSearchQueryBuilder
{
    public static string Build(
        IEnumerable<string>? authorNames,
        string? bookName,
        string? fileName,
        SearchInitialsHandling initialsHandling = SearchInitialsHandling.AsStored)
    {
        var trimmedBookName = bookName?.Trim() ?? string.Empty;
        var trimmedAuthors = (authorNames ?? Enumerable.Empty<string>())
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
