namespace AudiobookManager.Database.Repositories;

/// <summary>
/// The slice of an audiobook the series detail's owned section renders: identity, book name,
/// series part (the one this book has in the series being viewed), year, author/narrator names, duration and the cover path. Projected in SQL (the
/// name lists are correlated subqueries) so a paged detail request never materializes a full
/// entity graph - Genres, Description-size blobs and all - for every book of the series, which
/// is what <c>GetBooksBySeriesAsync</c> used to do on every section request.
/// </summary>
public record SeriesOwnedBookRow(
    long Id,
    string BookName,
    string? SeriesPart,
    int Year,
    List<string> Authors,
    List<string> Narrators,
    int? DurationInSeconds,
    string? CoverFilePath,
    bool IsMatched = false,
    string? MatchedSourceName = null,
    // The raw qualifiers column (see QualifierColumn) - parsed by the mapping layer.
    string Qualifiers = "",
    // The book's primary series (the mirrored column), so the series page can flag a book that is
    // listed here as a secondary series. Null for a book with no primary.
    string? PrimarySeries = null);
