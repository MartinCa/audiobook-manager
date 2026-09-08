namespace AudiobookManager.Database.Repositories;

/// <summary>
/// The slice of an audiobook the series detail's owned section renders: identity, book name,
/// series part, year, author/narrator names and duration. Projected in SQL (the name lists are
/// correlated subqueries) so a paged detail request never materializes a full entity graph -
/// Genres, Description-size blobs and all - for every book of the series, which is what
/// <c>GetBooksBySeriesAsync</c> used to do on every section request.
/// </summary>
public record SeriesOwnedBookRow(
    long Id,
    string BookName,
    string? SeriesPart,
    int Year,
    List<string> Authors,
    List<string> Narrators,
    int? DurationInSeconds);